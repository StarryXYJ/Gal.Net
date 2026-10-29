using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GalNet.Probes.Sdl3;

internal sealed class ProbeRun
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly List<object> _checks = [];
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private int _pumpCount;
    internal string? Failure { get; set; }

    internal async Task ExecuteAsync(Window window)
    {
        Check(SdlNative.GetVersion() == 3_004_018, "Pinned native version", SdlNative.GetVersion());
        Check(Marshal.SizeOf<SdlNative.VirtualJoystickDesc>() == 136, "64-bit virtual descriptor ABI", 136);
        Check(SdlNative.WasInit(0) == 0, "No preexisting SDL subsystem", 0);
        Check(SdlNative.SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1") != 0,
            "SDL collects device state; Avalonia owns focus gating");
        window.Activate();
        await Until(() => window.IsActive, "Initial Avalonia focus");
        uint previousVirtualId = 0;
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var initialized = false;
            uint id = 0;
            nint joystick = 0, gamepad = 0, name = 0;
            try
            {
                Check(SdlNative.Init(SdlNative.GamepadSubsystem) != 0, "Init gamepad-only: " + SdlNative.Error);
                initialized = true;
                Check(SdlNative.IsMainThread() != 0, "Avalonia UI thread is SDL main thread", _uiThread);
                var flags = SdlNative.WasInit(0);
                Check((flags & SdlNative.GamepadSubsystem) != 0 &&
                      (flags & (SdlNative.VideoSubsystem | SdlNative.AudioSubsystem)) == 0,
                    "Gamepad initialized without SDL video/audio", new { cycle, flags });
                _checks.Add(new { name = "Existing standard devices (not exercised)", ids = SdlNative.Gamepads() });
                name = Marshal.StringToCoTaskMemUTF8("GalNet SDL probe virtual controller");
                var desc = new SdlNative.VirtualJoystickDesc
                {
                    Version = 136, Type = 1, AxisCount = 6, ButtonCount = 15,
                    ButtonMask = (1u << 15) - 1, AxisMask = 63, Name = name
                };
                id = SdlNative.AttachVirtualJoystick(in desc);
                Check(id != 0 && id > previousVirtualId, "Attach virtual controller with fresh ID", id);
                previousVirtualId = id;
                joystick = SdlNative.OpenJoystick(id);
                Check(joystick != 0, "Open virtual joystick");
                Pump();
                Check(SdlNative.Gamepads().Contains(id), "Virtual joystick recognized as standard gamepad", id);
                gamepad = SdlNative.OpenGamepad(id);
                Check(gamepad != 0, "Open standard gamepad");
                Check(SdlNative.GamepadConnected(gamepad) != 0, "Connected gamepad");
                Check(SdlNative.SetVirtualButton(joystick, 0, 1) != 0, "Inject South press");
                Check(SdlNative.SetVirtualAxis(joystick, 0, 25000) != 0, "Inject left-stick right");
                await Until(() => SdlNative.GetButton(gamepad, 0) != 0 &&
                                  SdlNative.GetAxis(gamepad, 0) == 25000, "Pump publishes button and axis");
                Check(VisibleButtons(window, gamepad).SetEquals(["South", "Right"]),
                    "Focused probe sees standard positions");
                if (cycle == 0) await FocusRoundTrip(window, gamepad);
                Check(SdlNative.SetVirtualButton(joystick, 0, 0) != 0, "Inject South release");
                Check(SdlNative.SetVirtualAxis(joystick, 0, -25000) != 0, "Inject left-stick left");
                await Until(() => SdlNative.GetButton(gamepad, 0) == 0 &&
                                  SdlNative.GetAxis(gamepad, 0) == -25000, "Pump publishes release and opposite axis");
                Check(SdlNative.SetVirtualButton(joystick, 0, 1) != 0, "Inject held button before disconnect");
                Pump();
                Check(SdlNative.DetachVirtualJoystick(id) != 0, "Detach while pressed");
                var removedId = id;
                id = 0;
                await Until(() => SdlNative.GamepadConnected(gamepad) == 0 &&
                                  !SdlNative.Gamepads().Contains(removedId), "Pump observes disconnect");
                Check(VisibleButtons(window, gamepad).Count == 0, "Disconnected probe publishes empty input");
                Check((SdlNative.WasInit(0) & SdlNative.VideoSubsystem) == 0,
                    "SDL video remains uninitialized throughout the cycle");
            }
            finally
            {
                // Close all handles before dropping our subsystem reference.
                if (gamepad != 0) SdlNative.CloseGamepad(gamepad);
                if (joystick != 0) SdlNative.CloseJoystick(joystick);
                if (id != 0) SdlNative.DetachVirtualJoystick(id);
                if (name != 0) Marshal.FreeCoTaskMem(name);
                if (initialized) SdlNative.Quit(SdlNative.GamepadSubsystem);
            }
            Check(SdlNative.WasInit(0) == 0, "Owned subsystems released; next cycle reinitializes", cycle);
        }
    }

    private async Task FocusRoundTrip(Window window, nint gamepad)
    {
        var sink = new Window { Title = "GalNet SDL probe focus target", Width = 240, Height = 100 };
        try
        {
            sink.Show(window);
            sink.Activate();
            await Until(() => !window.IsActive && sink.IsActive, "Real Avalonia focus loss");
            Check(SdlNative.GetButton(gamepad, 0) != 0 && VisibleButtons(window, gamepad).Count == 0,
                "Held native input suppressed by Avalonia focus gate");
        }
        finally { sink.Close(); }
        window.Activate();
        await Until(() => window.IsActive, "Real Avalonia focus return");
    }

    // Probe gate only. Production action drafts, cancellation and rearming are
    // intentionally left to the later GameHost adapter and its own tests.
    private static HashSet<string> VisibleButtons(Window window, nint gamepad)
    {
        HashSet<string> buttons = [];
        if (!window.IsActive || SdlNative.GamepadConnected(gamepad) == 0) return buttons;
        if (SdlNative.GetButton(gamepad, 0) != 0) buttons.Add("South");
        if (SdlNative.GetAxis(gamepad, 0) > 16000) buttons.Add("Right");
        if (SdlNative.GetAxis(gamepad, 0) < -16000) buttons.Add("Left");
        return buttons;
    }

    private void Pump()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (Environment.CurrentManagedThreadId != _uiThread)
            throw new InvalidOperationException("SDL pump left the initializing UI thread.");
        SdlNative.PumpEvents();
        // This isolated process owns SDL's entire event queue. Production code
        // must define ownership before filtering/draining another SDL user's events.
        SdlNative.FlushEvents(0, 0xFFFF);
        _pumpCount++;
    }

    private async Task Until(Func<bool> predicate, string name)
    {
        var timeout = Stopwatch.StartNew();
        while (true)
        {
            if ((SdlNative.WasInit(0) & SdlNative.GamepadSubsystem) != 0) Pump();
            if (predicate()) { Check(true, name); return; }
            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException(name + ": " + SdlNative.Error);
            await Task.Delay(25);
        }
    }

    private void Check(bool passed, string name, object? value = null)
    {
        _checks.Add(new { name, passed, value });
        if (!passed) throw new InvalidOperationException(name + ": " + SdlNative.Error);
    }

    internal void WriteResult()
    {
        var result = new
        {
            passed = Failure is null, error = Failure, uiThread = _uiThread, pumpCount = _pumpCount,
            native = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Program.NativeManifest)),
            checks = _checks, physicalControllerTested = false,
            productionAdapterReplaced = false, utc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Program.ResultPath, JsonSerializer.Serialize(result, JsonOptions));
        Console.WriteLine($"SDL3 probe {(Failure is null ? "passed" : "FAILED")}; {_checks.Count} checks; {Program.ResultPath}");
    }
}
