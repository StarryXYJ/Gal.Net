using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Input;
using GalNet.Input.Sdl3;
using GalNet.Runtime.Logging;

namespace GalNet.Avalonia.GameHost;

/// <summary>UI-thread SDL input adapter. Only neutral device snapshots cross into GameView.</summary>
internal sealed class AvaloniaGamepadInput : IDisposable
{
    private readonly PlayerInputSettings _input;
    private readonly GamepadInputSession _session;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private Window? _window;
    private SdlGamepadSource? _source;
    private bool _disposed;

    public AvaloniaGamepadInput(PlayerInputSettings input)
    {
        _input = input;
        _session = new(input);
        _timer.Tick += Tick;
    }

    public void Bind(Window? window)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || ReferenceEquals(_window, window)) return;
        _timer.Stop();
        if (_window is not null)
        {
            _window.Deactivated -= Deactivated;
            _window.Closed -= Closed;
        }
        _source?.Dispose();
        _source = null;
        _session.Reset();
        _window = window;
        if (window is null) { _input.SetGamepadAvailability(false, null); return; }
        window.Deactivated += Deactivated;
        window.Closed += Closed;
        var source = new SdlGamepadSource();
        _source = source;
        _input.SetGamepadAvailability(source.IsAvailable, source.UnavailableReason);
        if (!source.IsAvailable)
            GameLog.Logger.Warning("SDL gamepad input unavailable: {Reason}", source.UnavailableReason);
        else
        {
            GameLog.Logger.Information("SDL 3.4.18 gamepad input initialized. NativeLibrary={NativeLibrary}", source.NativeLibraryPath);
            _timer.Start();
        }
    }

    private void Tick(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_source is not { } source) return;
        var devices = source.Poll();
        _session.Update(devices, _window?.IsActive == true);
        if (source.IsAvailable) return;
        _timer.Stop();
        _session.Cancel();
        _input.SetGamepadAvailability(false, source.UnavailableReason);
        GameLog.Logger.Warning("SDL gamepad input stopped: {Reason}", source.UnavailableReason);
    }

    private void Deactivated(object? sender, EventArgs args) => _session.Cancel();
    private void Closed(object? sender, EventArgs args) => Bind(null);

    public void Dispose()
    {
        if (_disposed) return;
        if (_window is not null) Bind(null);
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Tick;
        _source?.Dispose();
        _source = null;
        _session.Reset();
    }
}
