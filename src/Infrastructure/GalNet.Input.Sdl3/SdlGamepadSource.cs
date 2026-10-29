using System.Globalization;
using System.Runtime.InteropServices;
using GalNet.Presentation.Abstractions.Input;

namespace GalNet.Input.Sdl3;

/// <summary>Desktop SDL input lease. All leases share one UI thread and own only
/// the joystick/gamepad subsystems. SDL windows, rendering and the application loop are never created.</summary>
public sealed class SdlGamepadSource : IStandardGamepadSource
{
    private static readonly object Gate = new();
    private static Session? _shared;
    private Session? _session;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public bool IsAvailable => _session is not null;
    public string? UnavailableReason { get; private set; }
    public string? NativeLibraryPath => _session is null ? null : SdlNative.LoadedPath;

    public SdlGamepadSource()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsBrowser() ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            UnavailableReason = "SDL desktop input has not been integrated on this platform.";
            return;
        }
        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            UnavailableReason = "SDL desktop input currently requires a 64-bit process.";
            return;
        }
        try
        {
            lock (Gate)
            {
                if (_shared is not null && _shared.Thread != _thread)
                    throw new InvalidOperationException("SDL gamepad leases must share the initializing UI thread.");
                _shared ??= new Session(_thread);
                _shared.Leases++;
                _session = _shared;
            }
        }
        catch (Exception exception) when (IsNativeFailure(exception)) { UnavailableReason = exception.Message; }
    }

    public IReadOnlyList<StandardGamepadSnapshot> Poll()
    {
        VerifyThread();
        if (_session is null) return [];
        try { return _session.Poll(); }
        catch (Exception exception) when (IsNativeFailure(exception))
        {
            UnavailableReason = exception.Message;
            Dispose();
            return [];
        }
    }

    private static bool IsNativeFailure(Exception exception) => exception is
        DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException;

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("SDL input must be polled and disposed on its initializing UI thread.");
    }

    public void Dispose()
    {
        VerifyThread();
        lock (Gate)
        {
            if (_session is not { } session) return;
            _session = null;
            if (--session.Leases != 0) return;
            try { session.Dispose(); }
            finally { _shared = null; }
        }
    }

    private sealed class Session : IDisposable
    {
        private const string BackgroundHint = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS";
        private readonly Dictionary<uint, nint> _handles = [];
        private readonly string? _previousBackground;
        internal int Thread { get; }
        internal int Leases { get; set; }

        internal Session(int thread)
        {
            Thread = thread;
            if (SdlNative.GetVersion() != SdlNative.PinnedVersion)
                throw new InvalidOperationException("SDL native version must be exactly 3.4.18.");
            if (SdlNative.WasInit(SdlNative.Joystick | SdlNative.Gamepad) != 0)
                throw new InvalidOperationException("SDL input already has an external owner; coordinate ownership before enabling GalNet input.");
            if (SdlNative.WasInit(0) != 0 && SdlNative.IsMainThread() == 0)
                throw new InvalidOperationException("Existing SDL subsystems belong to another main thread.");
            _previousBackground = SdlNative.GetHint(BackgroundHint);
            if (SdlNative.SetHint(BackgroundHint, "1") == 0)
                throw new InvalidOperationException("SDL background state collection could not be enabled; Avalonia owns the focus gate.");
            if (SdlNative.Init(SdlNative.Gamepad) == 0)
            {
                var reason = SdlNative.Error;
                RestoreHint();
                throw new InvalidOperationException(reason);
            }
        }

        internal unsafe List<StandardGamepadSnapshot> Poll()
        {
            if (SdlNative.IsMainThread() == 0) throw new InvalidOperationException("SDL input left its main thread.");
            SdlNative.PumpEvents();
            SdlNative.UpdateGamepads();
            // We exclusively own SDL input (including its lifecycle events),
            // while other SDL subsystems and user events remain untouched.
            // State-event switches alone do not account for all queued input.
            SdlNative.FlushEvents(0x600, 0x6ff);
            var pointer = SdlNative.GetGamepads(out var count);
            if (pointer == 0) throw new InvalidOperationException(SdlNative.Error);
            uint[] ids;
            try { ids = new ReadOnlySpan<uint>((void*)pointer, count).ToArray(); }
            finally { SdlNative.Free(pointer); }
            foreach (var id in _handles.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                SdlNative.CloseGamepad(_handles[id]);
                _handles.Remove(id);
            }
            List<StandardGamepadSnapshot> snapshots = [];
            foreach (var id in ids.Order())
            {
                if (!_handles.TryGetValue(id, out var handle))
                {
                    handle = SdlNative.OpenGamepad(id);
                    if (handle == 0) continue; // A device can disappear between enumeration and open.
                    _handles.Add(id, handle);
                }
                if (SdlNative.Connected(handle) == 0) continue;
                var buttons = StandardGamepadButtons.None;
                for (var button = 0; button < 15; button++)
                    if (SdlNative.GetButton(handle, button) != 0) buttons |= (StandardGamepadButtons)(1 << button);
                var labels = new GamepadFaceLabels(Label(handle, 0), Label(handle, 1), Label(handle, 2), Label(handle, 3));
                var layout = SdlNative.GetType(handle) switch
                {
                    1 => GamepadLayout.Standard, 2 or 3 => GamepadLayout.Xbox,
                    4 or 5 or 6 => GamepadLayout.PlayStation, 7 or 8 or 9 or 10 => GamepadLayout.Nintendo,
                    11 => GamepadLayout.GameCube, 12 => GamepadLayout.Steam, _ => GamepadLayout.Unknown
                };
                snapshots.Add(new("sdl3:" + id.ToString(CultureInfo.InvariantCulture),
                    Marshal.PtrToStringUTF8(SdlNative.GetName(handle)) ?? "Gamepad", layout, labels, buttons,
                    Stick(handle, 0), Stick(handle, 1), Stick(handle, 2), Stick(handle, 3),
                    Trigger(handle, 4), Trigger(handle, 5)));
            }
            return snapshots;
        }

        private static GamepadButtonLabel Label(nint handle, int button)
        {
            var label = SdlNative.GetLabel(handle, button);
            return label is >= 0 and <= 8 ? (GamepadButtonLabel)label : GamepadButtonLabel.Unknown;
        }
        private static float Stick(nint handle, int axis)
        {
            var value = SdlNative.GetAxis(handle, axis);
            return value / (value < 0 ? 32768f : 32767f);
        }
        private static float Trigger(nint handle, int axis) => Math.Clamp(SdlNative.GetAxis(handle, axis) / 32767f, 0, 1);
        private void RestoreHint()
        {
            if (SdlNative.GetHint(BackgroundHint) != "1") return;
            if (_previousBackground is null) SdlNative.ResetHint(BackgroundHint);
            else SdlNative.SetHint(BackgroundHint, _previousBackground);
        }

        public void Dispose()
        {
            foreach (var handle in _handles.Values) SdlNative.CloseGamepad(handle);
            _handles.Clear();
            SdlNative.Quit(SdlNative.Gamepad);
            SdlNative.FlushEvents(0x600, 0x6ff);
            RestoreHint();
        }
    }
}
