"""Guard against backend checks reporting success without execution evidence."""
import importlib.util
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

SCRIPT = Path(__file__).with_name("verify-backends.py")
REPO = SCRIPT.resolve().parents[2]
spec = importlib.util.spec_from_file_location("backend_checks", SCRIPT)
backend = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backend)


def trx(path, outcome="Completed", **counts):
    values = dict(total=5, executed=3, passed=3, failed=0, notExecuted=2)
    values.update(counts)
    attributes = " ".join(f'{key}="{value}"' for key, value in values.items())
    path.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                    f'<ResultSummary outcome="{outcome}"><Counters {attributes}/>'
                    '</ResultSummary></TestRun>', encoding="utf-8")


class BackendChecksTests(unittest.TestCase):
    def setUp(self):
        parent = REPO / "out"
        parent.mkdir(exist_ok=True)
        self.root = parent / (".backend-tests-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.output = self.root / "evidence"
        self.native = self.root / "native-audio"
        self.native.write_bytes(b"test fixture")
        self.sdk = json.loads((REPO / "global.json").read_text())["sdk"]["version"]
        self.commands = []

    def tearDown(self):
        self.assertEqual(self.root.resolve().parent, (REPO / "out").resolve())
        self.assertFalse(self.root.is_symlink())
        shutil.rmtree(self.root)

    def fake_run(self, command, cwd, environment, log, steps, timeout=1200):
        command = [str(value) for value in command]
        self.commands.append(command)
        steps.append({"command": command, "exitCode": 0, "log": str(log)})
        text = self.sdk if command[1] == "--version" else "AOT_ACCEPTANCE_OK"
        log.write_text(text, encoding="utf-8")
        if command[1] == "test":
            name = Path(command[2]).stem
            trx(self.output / "results" / (name + ".trx"))

    def report(self):
        return json.loads((self.output / "backend-results.json").read_text())

    def verify(self, runner=None, **kwargs):
        with patch.object(backend, "run", runner or self.fake_run):
            return backend.verify(REPO, self.output, kwargs.pop("native", self.native), **kwargs)

    def test_existing_evidence_is_preserved_before_launch(self):
        self.output.mkdir()
        marker = self.output / "keep.txt"
        marker.write_text("keep")
        with patch.object(backend, "run") as run, self.assertRaises(ValueError):
            backend.verify(REPO, self.output, self.native)
        run.assert_not_called()
        self.assertEqual(list(self.output.iterdir()), [marker])
        self.assertEqual(marker.read_text(), "keep")

    def test_output_cannot_enclose_repository(self):
        for output in (REPO, REPO.parent):
            with self.subTest(output=output), self.assertRaises(ValueError):
                backend.verify(REPO, output, self.native)

    def test_wrong_sdk_leaves_checks_unexecuted(self):
        self.sdk = "0.0.0"
        with self.assertRaisesRegex(ValueError, "SDK must be exactly"):
            self.verify()
        self.assertFalse(self.report()["passed"])
        self.assertTrue(all(check["status"] == "not_run" for check in self.report()["checks"]))
        self.assertEqual(len(self.commands), 1)

    def test_missing_native_is_failure_not_skip(self):
        with self.assertRaises(FileNotFoundError):
            self.verify(native=self.root / "missing")
        self.assertFalse(self.report()["passed"])
        self.assertTrue(all(check["status"] == "not_run" for check in self.report()["checks"]))

    def test_native_build_failure_keeps_evidence(self):
        def fail_build(*args, **kwargs):
            if str(args[0][0]) == "cmake":
                raise RuntimeError("native build failed")
            self.fake_run(*args, **kwargs)
        with self.assertRaisesRegex(RuntimeError, "native build failed"):
            self.verify(fail_build, native=None)
        self.assertEqual(self.report()["error"], "native build failed")
        self.assertTrue(all(check["status"] == "not_run" for check in self.report()["checks"]))

    def test_success_records_counts_and_runtime_boundaries(self):
        report = self.verify()
        self.assertTrue(report["passed"])
        self.assertEqual(len(report["checks"]), len(backend.SUITES) + 1)
        for check in report["checks"][:-1]:
            self.assertEqual(check["status"], "passed")
            self.assertEqual(check["tests"]["executed"], 3)
            self.assertEqual(check["tests"]["notExecuted"], 2)
        for key in ("physicalAudioDeviceTested", "guiRuntimeTested", "nativeAotTested"):
            self.assertFalse(report[key])
        restores = [command for command in self.commands if command[1] == "restore"]
        self.assertEqual(len(restores), len(report["checks"]))
        self.assertTrue(all("--locked-mode" in command for command in restores))
        builds = [command for command in self.commands if command[1] == "build"]
        self.assertIn("-p:JsonSerializerIsReflectionEnabledByDefault=false", builds[0])
        self.assertIn("--aot-self-test", self.commands[-1])
        self.assertNotIn("--expect-aot", self.commands[-1])

    def test_failed_suite_still_runs_independent_checks(self):
        def fail_suite(*args, **kwargs):
            self.fake_run(*args, **kwargs)
            if args[0][1] == "test" and backend.SUITES[0] in str(args[0][2]):
                raise RuntimeError("test process failed")
        with self.assertRaises(RuntimeError):
            self.verify(fail_suite)
        report = self.report()
        self.assertFalse(report["passed"])
        self.assertEqual(report["checks"][0]["status"], "failed")
        self.assertTrue(all(check["status"] == "passed" for check in report["checks"][1:]))

    def test_missing_trx_fails_even_when_test_process_succeeds(self):
        def no_results(*args, **kwargs):
            self.fake_run(*args, **kwargs)
            if args[0][1] == "test":
                (self.output / "results" / (Path(args[0][2]).stem + ".trx")).unlink()
        with self.assertRaises(RuntimeError):
            self.verify(no_results)
        self.assertTrue(all(check["status"] == "failed" for check in self.report()["checks"][:-1]))
        self.assertEqual(self.report()["checks"][-1]["status"], "passed")

    def test_headless_requires_marker_and_successful_exit(self):
        for mode in ("missing_marker", "failed_exit"):
            with self.subTest(mode=mode):
                self.output = self.root / mode
                def fail_headless(*args, **kwargs):
                    self.fake_run(*args, **kwargs)
                    if "--aot-self-test" in args[0]:
                        if mode == "failed_exit":
                            raise RuntimeError("process failed after printing marker")
                        args[3].write_text("started but not accepted", encoding="utf-8")
                with self.assertRaises(RuntimeError):
                    self.verify(fail_headless)
                self.assertFalse(self.report()["passed"])
                self.assertEqual(self.report()["checks"][-1]["status"], "failed")

    def test_interruption_records_active_and_unexecuted_checks(self):
        def interrupt(*args, **kwargs):
            if args[0][1] == "test":
                raise KeyboardInterrupt()
            self.fake_run(*args, **kwargs)
        with self.assertRaises(KeyboardInterrupt):
            self.verify(interrupt)
        report = self.report()
        self.assertFalse(report["passed"])
        self.assertEqual(report["checks"][0]["status"], "failed")
        self.assertTrue(all(check["status"] == "not_run" for check in report["checks"][1:]))

    def test_trx_requires_executed_successful_tests(self):
        path = self.root / "result.trx"
        for counts in (dict(total=0, executed=0, passed=0, notExecuted=0),
                       dict(executed=3, passed=2, failed=1),
                       dict(outcome="Aborted"),
                       dict(executed=3, passed=2),
                       dict(total=2, executed=3, passed=3)):
            with self.subTest(counts=counts):
                trx(path, **counts)
                with self.assertRaises(ValueError):
                    backend.read_test_counts(path)
        path.write_text("<TestRun/>", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Missing TRX counters"):
            backend.read_test_counts(path)

    def test_explicit_tests_are_visible_when_adapter_not_executed_counter_is_zero(self):
        path = self.root / "explicit.trx"
        trx(path, total=28, executed=26, passed=26, notExecuted=0)
        counts = backend.read_test_counts(path)
        self.assertEqual(counts["notRun"], 2)
        self.assertEqual(counts["notExecuted"], 0)

    def test_local_no_restore_is_explicit(self):
        report = self.verify(restore=False)
        self.assertEqual(report["restore"], "not_run")
        self.assertFalse(any(command[1] == "restore" for command in self.commands))


if __name__ == "__main__":
    unittest.main()
