"""Start one composed Developer Sample on X11/WSLg and confirm its own exit dialog."""
import argparse
import ctypes as c
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import uuid


class Client(c.Structure):
    _fields_ = [("type", c.c_int), ("serial", c.c_ulong), ("send_event", c.c_int), ("display", c.c_void_p),
                ("window", c.c_ulong), ("message_type", c.c_ulong), ("format", c.c_int), ("data", c.c_long * 5)]


class Key(c.Structure):
    _fields_ = [("type", c.c_int), ("serial", c.c_ulong), ("send_event", c.c_int), ("display", c.c_void_p),
                ("window", c.c_ulong), ("root", c.c_ulong), ("subwindow", c.c_ulong), ("time", c.c_ulong),
                ("x", c.c_int), ("y", c.c_int), ("x_root", c.c_int), ("y_root", c.c_int),
                ("state", c.c_uint), ("keycode", c.c_uint), ("same_screen", c.c_int)]


class Event(c.Union):
    _fields_ = [("client", Client), ("key", Key), ("pad", c.c_long * 24)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--application", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--raw-publish", action="store_true")
    parser.add_argument("--entry")
    parser.add_argument("--media-directory", type=Path)
    parser.add_argument("--player-project-id", help="Run a Player without arguments using isolated XDG data.")
    args = parser.parse_args()
    if args.raw_publish and (not args.entry or not args.media_directory):
        parser.error("Raw publishing requires --entry and --media-directory.")
    if args.player_project_id and not args.raw_publish:
        parser.error("The isolated Player probe requires --raw-publish.")
    root, output = args.application.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        parser.error("Use an empty evidence directory.")
    x = c.CDLL("libX11.so.6")
    x.XOpenDisplay.argtypes, x.XOpenDisplay.restype = [c.c_char_p], c.c_void_p
    x.XDefaultRootWindow.argtypes, x.XDefaultRootWindow.restype = [c.c_void_p], c.c_ulong
    x.XInternAtom.argtypes, x.XInternAtom.restype = [c.c_void_p, c.c_char_p, c.c_int], c.c_ulong
    x.XQueryTree.argtypes = [c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_ulong), c.POINTER(c.POINTER(c.c_ulong)), c.POINTER(c.c_uint)]
    x.XGetWindowProperty.argtypes = [c.c_void_p, c.c_ulong, c.c_ulong, c.c_long, c.c_long, c.c_int, c.c_ulong,
        c.POINTER(c.c_ulong), c.POINTER(c.c_int), c.POINTER(c.c_ulong), c.POINTER(c.c_ulong), c.POINTER(c.c_void_p)]
    x.XFetchName.argtypes = [c.c_void_p, c.c_ulong, c.POINTER(c.c_void_p)]
    x.XFree.argtypes, x.XFlush.argtypes, x.XCloseDisplay.argtypes = [c.c_void_p], [c.c_void_p], [c.c_void_p]
    x.XSendEvent.argtypes = [c.c_void_p, c.c_ulong, c.c_int, c.c_long, c.c_void_p]
    x.XKeysymToKeycode.argtypes, x.XKeysymToKeycode.restype = [c.c_void_p, c.c_ulong], c.c_uint
    display = x.XOpenDisplay(None)
    if not display:
        raise RuntimeError("An X11/WSLg display is required.")
    desktop = x.XDefaultRootWindow(display)
    pid_atom = x.XInternAtom(display, b"_NET_WM_PID", 0)
    title_atom = x.XInternAtom(display, b"_NET_WM_NAME", 0)

    def children_of(window):
        returned_root, parent, count = c.c_ulong(), c.c_ulong(), c.c_uint()
        children = c.POINTER(c.c_ulong)()
        if not x.XQueryTree(display, window, c.byref(returned_root), c.byref(parent), c.byref(children), c.byref(count)):
            return []
        try:
            return list(children[:count.value])
        finally:
            if children:
                x.XFree(children)

    def owned_windows(pid):
        found, pending = [], [(window, 0) for window in children_of(desktop)]
        while pending:
            window, depth = pending.pop()
            if depth < 4:
                pending.extend((child, depth + 1) for child in children_of(window))
            kind, items, remaining, fmt, data = c.c_ulong(), c.c_ulong(), c.c_ulong(), c.c_int(), c.c_void_p()
            x.XGetWindowProperty(display, window, pid_atom, 0, 1, 0, 0, c.byref(kind), c.byref(fmt), c.byref(items), c.byref(remaining), c.byref(data))
            try:
                if not data.value or items.value != 1 or c.cast(data, c.POINTER(c.c_ulong))[0] != pid:
                    continue
            finally:
                if data.value:
                    x.XFree(data)
            title = c.c_void_p()
            x.XFetchName(display, window, c.byref(title))
            try:
                name = c.string_at(title).decode("utf-8", errors="replace") if title.value else ""
            finally:
                if title.value:
                    x.XFree(title)
            if not name:
                kind, items, remaining, fmt, data = c.c_ulong(), c.c_ulong(), c.c_ulong(), c.c_int(), c.c_void_p()
                x.XGetWindowProperty(display, window, title_atom, 0, 512, 0, 0, c.byref(kind), c.byref(fmt), c.byref(items), c.byref(remaining), c.byref(data))
                try:
                    if data.value and fmt.value == 8:
                        name = c.string_at(data, items.value).decode("utf-8", errors="replace")
                finally:
                    if data.value:
                        x.XFree(data)
            if name.startswith("GalNet Avalonia"):
                found.append((window, name))
        return found

    report = {"passed": False, "application": str(root), "nativeDesktopTested": not Path("/dev/dxg").exists()}
    process = None
    try:
        layout = {"entryPoint": args.entry} if args.raw_publish else json.loads((root / "docs/application-layout.json").read_text())
        data = output / "data"
        profile = data / "GalNet" / hashlib.sha256(("project:" + args.player_project_id).encode()).hexdigest() \
            if args.player_project_id else output / "profile"
        profile.mkdir(parents=True)
        values = {}
        for name, value, kind in (("player.galnet.startup.launchCount", 1, 1), ("player.galnet.settings.locale", "en-US", 3)):
            values[name] = dict(Uid=uuid.uuid4().hex, Name=name, Type=kind, Value=value)
        (profile / "player-variables.json").write_text(json.dumps(values))
        environment = dict(os.environ, PATH="/usr/bin:/bin", DOTNET_ROOT=str(output / "no-sdk"), DOTNET_MULTILEVEL_LOOKUP="0")
        for key in ("DOTNET_ROOT_X64", "DOTNET_HOST_PATH", "LD_LIBRARY_PATH", "VLC_PLUGIN_PATH"):
            environment.pop(key, None)
        if args.raw_publish:
            media = args.media_directory.resolve()
            environment.update(LD_LIBRARY_PATH=str(media), VLC_PLUGIN_PATH=str(media / "plugins"))
            report.update(rawPublish=True, mediaDirectory=str(media), minimumOsTested=False)
        if args.player_project_id:
            environment["XDG_DATA_HOME"] = str(data)
        if Path("/dev/dxg").exists():
            environment.setdefault("GALLIUM_DRIVER", "d3d12")
        arguments = [] if args.player_project_id else ["--profile", str(profile)]
        with (output / "stdout.log").open("w") as stdout, (output / "stderr.log").open("w") as stderr:
            process = subprocess.Popen([str(root / layout["entryPoint"]), *arguments],
                                       cwd=output, env=environment, stdout=stdout, stderr=stderr)
            deadline = time.monotonic() + 45
            windows, ready = [], False
            while process.poll() is None and time.monotonic() < deadline:
                windows = owned_windows(process.pid)
                ready = len(windows) == 1 and any("Session initialized. SaveSlots=" in log.read_text()
                                                 for log in (profile / "logs").glob("game-*.log"))
                if ready:
                    break
                time.sleep(.1)
            if process.poll() is None:
                (output / "modules.txt").write_text(Path(f"/proc/{process.pid}/maps").read_text())
            report["observedWindows"] = windows
            if not ready:
                raise RuntimeError(f"Sample did not create its window/session; exit={process.poll()}.")
            maps = Path(f"/proc/{process.pid}/maps").read_text()
            (output / "modules.txt").write_text(maps)
            libraries = (root / "libcoreclr.so", args.media_directory.resolve() / "libvlc.so.5", root / "libSDL3.so.0") \
                if args.raw_publish else (root / "lib/libcoreclr.so", root / "lib/native/libvlc.so.5", root / "lib/libSDL3.so.0")
            for library in libraries:
                if str(library) not in maps:
                    raise RuntimeError(f"Expected private runtime was not loaded: {library}")
            event = Event()
            event.client.type, event.client.display, event.client.window = 33, display, windows[0][0]
            event.client.message_type, event.client.format = x.XInternAtom(display, b"WM_PROTOCOLS", 0), 32
            event.client.data[0] = x.XInternAtom(display, b"WM_DELETE_WINDOW", 0)
            if not x.XSendEvent(display, windows[0][0], 0, 0, c.byref(event)):
                raise RuntimeError("The owned window did not receive its close request.")
            x.XFlush(display)
            time.sleep(.3)
            for symbol in (0xff09, 0xff0d):
                for kind, mask in ((2, 1), (3, 2)):
                    key = Event()
                    key.key.type, key.key.display, key.key.window, key.key.root = kind, display, windows[0][0], desktop
                    key.key.keycode, key.key.same_screen = x.XKeysymToKeycode(display, symbol), 1
                    if not x.XSendEvent(display, windows[0][0], 0, mask, c.byref(key)):
                        raise RuntimeError("The owned window did not receive exit confirmation input.")
                x.XFlush(display)
                time.sleep(.1)
            report["exitCode"] = process.wait(timeout=15)
            if process.returncode != 0:
                raise RuntimeError("Sample did not exit normally.")
            if json.loads((profile / "player-variables.json").read_text())["player.galnet.startup.launchCount"]["Value"] != 2:
                raise RuntimeError("Player launch count was not persisted.")
            report.update(passed=True, windowTitle=windows[0][1], sessionReady=True, sdkDiscoverable=False)
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        if process is not None and process.poll() is None:
            process.kill()
            process.wait()
        x.XCloseDisplay(display)
        (output / "verification.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
