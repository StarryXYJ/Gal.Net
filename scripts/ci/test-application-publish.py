"""Integration checks for application publish guards and owned-process cancellation."""
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

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
    try:
        state = Path(f"/proc/{pid}/stat").read_text().split(") ", 1)[1].split()[0]
        return state not in ("Z", "X")
    except FileNotFoundError:
        return False


class ApplicationPublishTests(unittest.TestCase):
    def setUp(self):
        parent = (REPO / "out").resolve()
        parent.mkdir(exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix=".application-tests-", dir=parent)
        self.root = Path(self.temporary.name).resolve()
        self.assertEqual(self.root.parent, parent)

    def tearDown(self):
        self.temporary.cleanup()

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


if __name__ == "__main__":
    unittest.main()
