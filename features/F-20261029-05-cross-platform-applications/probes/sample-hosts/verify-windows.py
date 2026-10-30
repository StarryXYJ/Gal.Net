"""Start all four raw Sample variants with matched content and confirm their own exit dialogs."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--content-report", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    published, output = args.publish.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    project = json.loads(args.content_report.read_text())["projectId"]
    key = hashlib.sha256(("project:" + project).encode()).hexdigest()
    profile_root = Path(os.environ["LOCALAPPDATA"]) / "GalNet"
    default_profile = profile_root / key
    if default_profile.exists():
        raise ValueError("Use a fresh probe project identity; existing player profiles are preserved.")
    user = c.WinDLL("user32", use_last_error=True)
    kernel, psapi = c.WinDLL("kernel32", use_last_error=True), c.WinDLL("psapi", use_last_error=True)
    callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    user.EnumWindows.argtypes = [callback, w.LPARAM]
    user.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
    user.IsWindowVisible.argtypes = [w.HWND]
    user.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
    user.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
    kernel.OpenProcess.argtypes, kernel.OpenProcess.restype = [w.DWORD, w.BOOL, w.DWORD], w.HANDLE
    kernel.CloseHandle.argtypes = [w.HANDLE]
    psapi.EnumProcessModules.argtypes = [w.HANDLE, c.POINTER(w.HMODULE), w.DWORD, c.POINTER(w.DWORD)]
    psapi.GetModuleFileNameExW.argtypes = [w.HANDLE, w.HMODULE, w.LPWSTR, w.DWORD]

    def windows(pid):
        found = []

        @callback
        def visit(hwnd, _):
            owner = w.DWORD()
            user.GetWindowThreadProcessId(hwnd, c.byref(owner))
            title = c.create_unicode_buffer(512)
            user.GetWindowTextW(hwnd, title, len(title))
            if owner.value == pid and user.IsWindowVisible(hwnd) and title.value.startswith("GalNet"):
                found.append((hwnd, title.value))
            return True

        if not user.EnumWindows(visit, 0):
            raise c.WinError(c.get_last_error())
        return found

    def modules(pid):
        handle = kernel.OpenProcess(0x410, False, pid)
        if not handle:
            raise c.WinError(c.get_last_error())
        try:
            entries, count = (w.HMODULE * 2048)(), w.DWORD()
            if not psapi.EnumProcessModules(handle, entries, c.sizeof(entries), c.byref(count)):
                raise c.WinError(c.get_last_error())
            paths = []
            for module in entries[:count.value // c.sizeof(w.HMODULE)]:
                path = c.create_unicode_buffer(32768)
                if not psapi.GetModuleFileNameExW(handle, module, path, len(path)):
                    raise c.WinError(c.get_last_error())
                paths.append(path.value)
            return paths
        finally:
            kernel.CloseHandle(handle)

    def seed(profile):
        profile.mkdir(parents=True, exist_ok=False)
        values = {}
        for name, value, kind in (("player.galnet.startup.launchCount", 1, 1),
                                  ("player.galnet.settings.locale", "en-US", 3)):
            values[name] = dict(Uid=uuid.uuid4().hex, Name=name, Type=kind, Value=value)
        (profile / "player-variables.json").write_text(json.dumps(values))

    seed(default_profile)
    report = {"passed": False, "projectId": project, "defaultProfile": str(default_profile), "runs": []}
    try:
        for kind, assembly in (("Sample", "GalNet.Sample.Avalonia"),
                               ("CrossPlatform", "GalNet.Sample.CrossPlatform.Desktop")):
            for flavor in ("Player", "Developer"):
                role = kind + "-" + flavor
                app_root, evidence = published / role, output / role
                evidence.mkdir()
                profile = default_profile if flavor == "Player" else evidence / "profile"
                if flavor == "Developer":
                    seed(profile)
                def ready_messages():
                    return sum(log.read_text(encoding="utf-8-sig").count("Session initialized. SaveSlots=")
                               for log in (profile / "logs").glob("game-*.log"))
                prior_ready = ready_messages()
                variables = profile / "player-variables.json"
                prior_launches = json.loads(variables.read_text())["player.galnet.startup.launchCount"]["Value"]
                environment = dict(os.environ, PATH=str(app_root) + os.pathsep + os.environ["SystemRoot"] + "/System32",
                                   DOTNET_ROOT=str(evidence / "no-sdk"), DOTNET_MULTILEVEL_LOOKUP="0")
                environment["DOTNET_ROOT_X64"] = environment["DOTNET_ROOT"]
                command = [str(app_root / (assembly + ".exe"))]
                if flavor == "Developer":
                    command += ["--profile", str(profile)]
                record = {"role": role, "profile": str(profile), "arguments": command[1:], "passed": False}
                report["runs"].append(record)
                process = None
                try:
                    with (evidence / "stdout.log").open("w") as stdout, (evidence / "stderr.log").open("w") as stderr:
                        process = subprocess.Popen(command, cwd=evidence, env=environment, stdout=stdout, stderr=stderr,
                                                   creationflags=subprocess.CREATE_NO_WINDOW)
                        deadline = time.monotonic() + 45
                        ready = False
                        while process.poll() is None and time.monotonic() < deadline:
                            owned = windows(process.pid)
                            ready = len(owned) == 1 and ready_messages() > prior_ready
                            if ready:
                                break
                            time.sleep(.1)
                        if not ready:
                            raise RuntimeError(f"Session/window not ready: {role}, exit={process.poll()}")
                        loaded = modules(process.pid)
                        (evidence / "modules.json").write_text(json.dumps(loaded, indent=2) + "\n")
                        expected = [app_root / "coreclr.dll", app_root / "SDL3.dll",
                                    app_root / "libvlc/win-x64/libvlc.dll"]
                        for library in expected:
                            if str(library).lower() not in {str(Path(path)).lower() for path in loaded}:
                                raise RuntimeError(f"Expected private runtime not loaded: {library}")
                        hwnd, title = owned[0]
                        if not user.PostMessageW(hwnd, 0x10, 0, 0):
                            raise c.WinError(c.get_last_error())
                        time.sleep(.3)
                        for keycode in (0x09, 0x0D):
                            for message in (0x100, 0x101):
                                if not user.PostMessageW(hwnd, message, keycode, 0):
                                    raise c.WinError(c.get_last_error())
                        record.update(exitCode=process.wait(timeout=15), title=title, sessionReady=True, sdkDiscoverable=False)
                        if process.returncode:
                            raise RuntimeError(f"Abnormal exit: {role}, code={process.returncode}")
                        launches = json.loads(variables.read_text())["player.galnet.startup.launchCount"]["Value"]
                        if launches != prior_launches + 1:
                            raise RuntimeError(f"Player progress was not persisted: {role}")
                        record["launchCount"] = launches
                        record["passed"] = True
                finally:
                    if process is not None and process.poll() is None:
                        process.kill()
                        process.wait(timeout=10)
                print(f"{role}: native session and normal confirmed exit passed", flush=True)
        report["passed"] = True
    finally:
        (output / "verification.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
