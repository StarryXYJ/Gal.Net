using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Input;
using GalNet.Input.Sdl3;
using GalNet.Presentation.Abstractions.Input;
using Native = GalNet.Probes.Sdl3.SdlNative;

namespace GalNet.Probes.Sdl3Host;

internal static class Program
{
    internal static string ResultPath = "";
    internal static bool ExpectUnavailable;
    internal static int ExitCode = 1;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2) return 2;
        ResultPath = Path.GetFullPath(args[0]);
        ExpectUnavailable = args.Length == 2 && args[1] == "--expect-unavailable";
        try
        {
            AppBuilder.Configure<ProbeApp>().UsePlatformDetect().WithInterFont()
                .With(new global::Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            File.WriteAllText(ResultPath, JsonSerializer.Serialize(new { passed = false, error = exception.ToString() }));
        }
        return ExitCode;
    }
}

internal sealed class ProbeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window { Title = "GalNet formal SDL host probe", Width = 440, Height = 160 };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                await Task.Delay(100);
                var checks = new List<object>();
                string? error = null;
                try { await new HostRun(checks).ExecuteAsync(window); Program.ExitCode = 0; }
                catch (Exception exception) { error = exception.ToString(); }
                finally
                {
                    File.WriteAllText(Program.ResultPath, JsonSerializer.Serialize(new
                    {
                        passed = error is null, error, checks, physicalControllerTested = false,
                        productionAdapterReplaced = true, utc = DateTimeOffset.UtcNow
                    }, JsonOptions));
                    desktop.Shutdown(Program.ExitCode);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

internal sealed partial class HostRun(List<object> checks)
{
    private const string ProbeLibrary = "galnet-sdl3-probe";
    private readonly int _thread = Environment.CurrentManagedThreadId;
    [LibraryImport(ProbeLibrary, EntryPoint = "SDL_HasEvents")]
    private static partial byte HasEvents(uint min, uint max);
    [LibraryImport(ProbeLibrary, EntryPoint = "SDL_RegisterEvents")]
    private static partial uint RegisterEvents(int count);
    [LibraryImport(ProbeLibrary, EntryPoint = "SDL_PushEvent")]
    private static partial byte PushEvent(in UserEvent value);
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct UserEvent { [FieldOffset(0)] internal uint Type; }

    internal async Task ExecuteAsync(Window window)
    {
        using var source = new SdlGamepadSource();
        using var input = new PlayerInputSettings();
        using var adapter = new AvaloniaGamepadInput(input);
        adapter.Bind(window);
        if (Program.ExpectUnavailable)
        {
            Check(!source.IsAvailable && !input.GamepadSupported, "Missing private SDL disables gamepad without aborting UI");
            Check(source.UnavailableReason?.Contains("Private SDL", StringComparison.Ordinal) == true,
                "Missing native failure is diagnostic", source.UnavailableReason);
            return;
        }
        Check(source.IsAvailable && input.GamepadSupported, "Formal backend and bound adapter available", source.UnavailableReason);
        var library = source.NativeLibraryPath!;
        var handle = NativeLibrary.Load(library);
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, _, _) => name == ProbeLibrary ? handle : 0);
        using (var stream = File.OpenRead(library))
            Check(true, "Actual private native library", new { path = library, sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)), version = Native.GetVersion() });
        Check((Native.WasInit(0) & (Native.VideoSubsystem | Native.AudioSubsystem)) == 0,
            "Formal backend initializes no SDL video/audio");
        var userEvent = new UserEvent { Type = RegisterEvents(1) };
        Check(userEvent.Type != 0 && PushEvent(in userEvent) != 0, "Queue unrelated SDL user event");
        var original = source.Poll();
        Check(original.Count == 0, "Isolated virtual probe requires no physical standard devices", original.Select(pad => pad.Name).ToArray());
        window.Activate();
        await Until(() => window.IsActive, "Initial native focus");
        var changes = new List<string>();
        var cancelled = 0;
        input.GamepadChanged += buttons => changes.Add(string.Join('+', buttons.Order()));
        input.GamepadCancelled += () => cancelled++;
        var name = Marshal.StringToCoTaskMemUTF8("GalNet formal SDL virtual controller");
        uint id = 0;
        nint joystick = 0;
        try
        {
            id = Attach(name);
            joystick = Native.OpenJoystick(id);
            Check(joystick != 0, "Open virtual joystick");
            Check(Native.SetVirtualAxis(joystick, 4, short.MinValue) != 0 &&
                  Native.SetVirtualAxis(joystick, 5, short.MinValue) != 0, "Neutral trigger injection");
            await Until(() => input.GamepadDeviceName == "GalNet formal SDL virtual controller", "Formal timer discovers hotplug");
            await Task.Delay(80);
            var snapshot = source.Poll().Single();
            Check(snapshot.Id == "sdl3:" + id && snapshot.LeftTrigger == 0 && snapshot.RightTrigger == 0,
                "Neutral snapshots and connection identity", snapshot);
            source.Dispose();
            Check((Native.WasInit(0) & Native.GamepadSubsystem) != 0, "Disposing one lease preserves the adapter's lease");
            Check(Native.SetVirtualButton(joystick, 0, 1) != 0, "Inject South");
            await Until(() => input.GamepadButtons.Contains("South"), "Actual adapter publishes South");
            await Task.Delay(90);
            Check(changes.SequenceEqual(["South"]), "Held state does not duplicate button edges", changes.ToArray());
            var beforeCancel = cancelled;
            var sink = new Window { Title = "GalNet formal probe focus target", Width = 240, Height = 100 };
            try
            {
                sink.Show(window);
                sink.Activate();
                await Until(() => sink.IsActive && !window.IsActive, "Actual native focus loss");
                Check(input.GamepadButtons.Count == 0 && cancelled > beforeCancel, "Deactivation immediately cancels held input");
                await Task.Delay(90);
                Check(changes.Count == 1, "Background timer does not publish input or synthetic release");
            }
            finally { sink.Close(); }
            window.Activate();
            await Until(() => window.IsActive, "Actual focus regain");
            await Task.Delay(100);
            Check(input.GamepadButtons.Count == 0 && changes.Count == 1, "Held input cannot fire after focus regain");
            Native.SetVirtualButton(joystick, 0, 0);
            await Task.Delay(90);
            Native.SetVirtualButton(joystick, 0, 1);
            await Until(() => input.GamepadButtons.Contains("South"), "Neutral sample rearms subsequent press");
            Check(Native.DetachVirtualJoystick(id) != 0, "Detach held virtual device");
            var previousId = id;
            id = 0;
            await Until(() => input.GamepadButtons.Count == 0 && input.GamepadDeviceName is null, "Actual adapter cancels disconnect");
            Check(changes.Count == 2, "Disconnect cancels rather than committing a release draft");
            Native.CloseJoystick(joystick);
            joystick = 0;
            id = Attach(name);
            Check(id > previousId, "Reattached controller has a fresh identity");
            joystick = Native.OpenJoystick(id);
            Native.SetVirtualAxis(joystick, 4, short.MinValue);
            Native.SetVirtualAxis(joystick, 5, short.MinValue);
            await Until(() => input.GamepadDeviceName is not null, "Adapter detects reattachment");
            await Task.Delay(80);
            Native.SetVirtualAxis(joystick, 0, 25000);
            await Until(() => input.GamepadButtons.Contains("Right"), "Adapter maps positive SDL X");
            using (var observer = new SdlGamepadSource())
            {
                var sample = observer.Poll().Single();
                Check(Math.Abs(sample.LeftX - 25000 / 32767f) < .0001f, "Formal axis normalization", sample.LeftX);
                var wrongThreadRejected = await Task.Run(() =>
                {
                    try { observer.Poll(); return false; }
                    catch (InvalidOperationException) { return true; }
                });
                Check(wrongThreadRejected, "Wrong-thread polling is rejected before touching SDL");
            }
            Check(HasEvents(0x600, 0x6ff) == 0, "State polling does not accumulate joystick/gamepad events");
            Check(HasEvents(userEvent.Type, userEvent.Type) != 0, "Input source preserves unrelated SDL user events");
        }
        finally
        {
            if (id != 0) Native.DetachVirtualJoystick(id);
            if (joystick != 0) Native.CloseJoystick(joystick);
            Marshal.FreeCoTaskMem(name);
            adapter.Dispose();
        }
        Check(Native.WasInit(0) == 0, "Last formal lease releases owned subsystems");
        Check(Native.Init(Native.GamepadSubsystem) != 0, "External input owner setup");
        try
        {
            using var externalConflict = new SdlGamepadSource();
            Check(!externalConflict.IsAvailable && externalConflict.UnavailableReason?.Contains("external owner", StringComparison.Ordinal) == true,
                "External SDL input owner is preserved and reported");
            Check((Native.WasInit(0) & Native.GamepadSubsystem) != 0, "Conflict does not quit external input");
        }
        finally { Native.Quit(Native.GamepadSubsystem); }
        const uint events = 0x4000;
        Check(Native.Init(events) != 0, "External events-only owner setup");
        try
        {
            var wrongInitThreadRejected = await Task.Run(() =>
            {
                using var wrong = new SdlGamepadSource();
                return !wrong.IsAvailable && wrong.UnavailableReason?.Contains("another main thread", StringComparison.Ordinal) == true;
            });
            Check(wrongInitThreadRejected, "Existing SDL main thread prevents wrong-thread input initialization");
            using (var sharedEvents = new SdlGamepadSource())
            {
                Check(sharedEvents.IsAvailable, "Same UI thread can share an external events-only subsystem");
                sharedEvents.Poll();
            }
            Check(Native.WasInit(0) == events, "Formal release preserves an external events-only reference");
        }
        finally { Native.Quit(events); }
        using (var retry = new SdlGamepadSource()) Check(retry.IsAvailable, "Formal source can initialize again after complete cleanup");
        Check(Native.WasInit(0) == 0, "Reinitialized lease also releases cleanly");
    }

    private static uint Attach(nint name)
    {
        var desc = new Native.VirtualJoystickDesc
        { Version = 136, Type = 1, AxisCount = 6, ButtonCount = 15, ButtonMask = 32767, AxisMask = 63, Name = name };
        var id = Native.AttachVirtualJoystick(in desc);
        if (id == 0) throw new InvalidOperationException(Native.Error);
        return id;
    }

    private async Task Until(Func<bool> predicate, string name)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException(name + ": " + Native.Error);
            await Task.Delay(25);
        }
        Check(true, name);
    }
    private void Check(bool passed, string name, object? value = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Probe left the UI thread.");
        checks.Add(new { name, passed, value });
        if (!passed) throw new InvalidOperationException(name + ": " + value);
    }
}
