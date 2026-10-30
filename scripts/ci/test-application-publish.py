"""Integration checks for application publish guards and owned-process cancellation."""
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import unittest
from unittest.mock import Mock, patch
import uuid

import process_steps

SCRIPT = Path(__file__).with_name("publish-applications.py")
REPO = SCRIPT.resolve().parents[2]
spec = importlib.util.spec_from_file_location("application_publish", SCRIPT)
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


def running(pid):
    if os.name == "nt":
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        handle = kernel.OpenProcess(0x1000, False, pid)
        if not handle:
            return False
        try:
            code = wintypes.DWORD()
            return bool(kernel.GetExitCodeProcess(handle, ctypes.byref(code))) and code.value == 259
        finally:
            kernel.CloseHandle(handle)
    process = subprocess.run(["ps", "-o", "stat=", "-p", str(pid)],
                             capture_output=True, text=True, check=False)
    state = process.stdout.strip()
    return process.returncode == 0 and bool(state) and state[0] not in ("Z", "X")


class ApplicationPublishTests(unittest.TestCase):
    def setUp(self):
        parent = (REPO / "out").resolve()
        parent.mkdir(exist_ok=True)
        self.root = parent / (".application-tests-" + uuid.uuid4().hex)
        self.root.mkdir()
        self.assertEqual(self.root.parent, parent)

    def tearDown(self):
        self.assertEqual(self.root.resolve().parent, (REPO / "out").resolve())
        self.assertFalse(self.root.is_symlink())
        shutil.rmtree(self.root)

    def cli(self, *arguments):
        return subprocess.run([sys.executable, str(SCRIPT), "--target", "win-x64" if os.name == "nt" else "linux-x64",
                               "--repository", str(REPO), *map(str, arguments)],
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=30)

    def test_existing_output_is_preserved_before_sdk_launch(self):
        output = self.root / "existing"
        output.mkdir()
        marker = output / "keep.txt"
        marker.write_text("keep")
        process = self.cli("--output", output, "--dotnet", self.root / "no-sdk")
        self.assertEqual(process.returncode, 2, process.stdout)
        self.assertEqual(list(output.iterdir()), [marker])
        self.assertEqual(marker.read_text(), "keep")

    def test_incorrect_sdk_is_rejected_before_native_build(self):
        output = self.root / "wrong-sdk"
        process = self.cli("--output", output, "--dotnet", sys.executable)
        self.assertEqual(process.returncode, 1, process.stdout)
        report = json.loads((output / "application-publish.json").read_text())
        self.assertFalse(report["passed"])
        self.assertIn("SDK must be exactly", report["error"])
        self.assertEqual(len(report["steps"]), 1)
        self.assertFalse((output / "native").exists())

    def test_wrong_native_payload_is_rejected_before_publish(self):
        output = self.root / "bad-native"
        invalid = self.root / "not-a-target-library"
        invalid.write_bytes(b"not a native library")
        process = self.cli("--output", output, "--native-audio", invalid,
                           "--dotnet", os.environ.get("GALNET_TEST_DOTNET", "dotnet"))
        self.assertEqual(process.returncode, 1, process.stdout)
        report = json.loads((output / "application-publish.json").read_text())
        self.assertFalse(report["passed"])
        self.assertEqual(report["sdkVersion"], json.loads((REPO / "global.json").read_text())["sdk"]["version"])
        self.assertEqual(len(report["steps"]), 1)
        self.assertFalse((output / "publish").exists())

    def test_sample_payload_rejects_debug_dependencies_and_stray_symbols(self):
        root = self.root / "player"
        root.mkdir()
        for kind in ("CrossPlatform",):
            role = kind + "-Player"
            self.assertFalse(publisher.validate_sample_capabilities(root, role, {})["gameDebugging"])
            for name in ("GalNet.Avalonia.Debugging", "GalNet.Debugging", "AvaloniaUI.DiagnosticsSupport"):
                with self.subTest(role=role, dependency=name), self.assertRaisesRegex(ValueError, "debug dependencies"):
                    publisher.validate_sample_capabilities(root, role, {name + "/1.0.0": {}})
            for name in ("GalNet.Avalonia.Debugging.dll", "GalNet.Debugging.dll",
                         "AvaloniaUI.DiagnosticsSupport.Avalonia.dll", "referenced/native.pdb"):
                stray = root / name
                stray.parent.mkdir(parents=True, exist_ok=True)
                stray.write_bytes(b"unexpected debug payload")
                with self.subTest(role=role, file=name), self.assertRaisesRegex(ValueError, "debug files or symbols"):
                    publisher.validate_sample_capabilities(root, role, {})
                stray.unlink()

    def test_developer_payload_requires_complete_debug_dependencies_but_not_symbols(self):
        root = self.root / "developer"
        root.mkdir()
        dependencies = {name + "/1.0.0": {} for name in (
            "GalNet.Avalonia.Debugging", "GalNet.Debugging", "AvaloniaUI.DiagnosticsSupport")}
        debug_assemblies = ("GalNet.Avalonia.Debugging.dll", "GalNet.Debugging.dll",
                            "AvaloniaUI.DiagnosticsSupport.Avalonia.dll")
        for kind in ("CrossPlatform",):
            role = kind + "-Developer"
            with self.subTest(role=role), self.assertRaisesRegex(ValueError, "missing debug dependencies"):
                publisher.validate_sample_capabilities(root, role, {})
            with self.subTest(role=role), self.assertRaisesRegex(FileNotFoundError, "debug assemblies"):
                publisher.validate_sample_capabilities(root, role, dependencies)
            for name in debug_assemblies:
                (root / name).write_bytes(b"debug assembly")
            self.assertFalse(publisher.validate_sample_capabilities(root, role, dependencies)["symbolsIncluded"])
            symbols = root / (Path(publisher.PROJECTS[kind]).stem + ".pdb")
            symbols.write_bytes(b"unexpected symbols")
            with self.subTest(role=role), self.assertRaisesRegex(ValueError, "debug files or symbols"):
                publisher.validate_sample_capabilities(root, role, dependencies)
            symbols.unlink()
            for name in debug_assemblies:
                (root / name).unlink()

    def test_timeout_stops_own_parent_and_child_and_preserves_unrelated_process(self):
        helper = self.root / "parent.py"
        pids = self.root / "pids.json"
        helper.write_text("import json,os,pathlib,subprocess,sys,time\n"
                          "child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(300)'])\n"
                          "pathlib.Path(sys.argv[1]).write_text(json.dumps([os.getpid(),child.pid]))\n"
                          "time.sleep(300)\n")
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(300)"],
                                     start_new_session=os.name != "nt",
                                     creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
        steps = []
        try:
            self.assertTrue(running(unrelated.pid))
            with self.assertRaises(subprocess.TimeoutExpired):
                publisher.run([sys.executable, str(helper), str(pids)], REPO, os.environ,
                              self.root / "cancel.log", steps, timeout=2)
            parent, child = json.loads(pids.read_text())
            deadline = time.monotonic() + 5
            while (running(parent) or running(child)) and time.monotonic() < deadline:
                time.sleep(.05)
            self.assertFalse(running(parent))
            self.assertFalse(running(child))
            self.assertIsNone(unrelated.poll())
            self.assertIsNotNone(steps[0]["exitCode"])
            self.assertNotEqual(steps[0]["exitCode"], 0)
        finally:
            unrelated.terminate()
            unrelated.wait(timeout=5)

    @unittest.skipUnless(os.name == "nt", "Windows taskkill failure guard")
    def test_failed_tree_termination_does_not_wait_forever(self):
        process = Mock(pid=12345)
        process.poll.return_value = None
        process.wait.side_effect = subprocess.TimeoutExpired("fixture", 1)
        denied = subprocess.CalledProcessError(1, "taskkill")
        steps = []
        with patch.object(process_steps.subprocess, "Popen", return_value=process), \
             patch.object(process_steps.subprocess, "run", side_effect=denied) as kill, \
             self.assertRaises(subprocess.CalledProcessError):
            publisher.run(["fixture"], REPO, os.environ, self.root / "denied.log", steps, timeout=1)
        process.wait.assert_called_once_with(timeout=1)
        self.assertTrue(kill.call_args.kwargs["check"])
        self.assertGreater(kill.call_args.kwargs["timeout"], 0)
        self.assertIsNone(steps[0]["exitCode"])
        self.assertIn("seconds", steps[0])


if __name__ == "__main__":
    unittest.main()
