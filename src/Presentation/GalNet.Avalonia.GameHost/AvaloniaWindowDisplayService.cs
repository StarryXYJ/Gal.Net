using Avalonia;
using Avalonia.Controls;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameHost;

/// <summary>Avalonia FullScreen means borderless fullscreen, not an exclusive display-mode switch.</summary>
public sealed class AvaloniaWindowDisplayService : IPlayerDisplayService, IDisposable
{
    private Window? _window;
    public bool IsAvailable => _window is not null;
    public IReadOnlyList<PlayerDisplayMode> Modes { get; } = Array.AsReadOnly(new[] { PlayerDisplayMode.Window, PlayerDisplayMode.BorderlessFullscreen });
    public IReadOnlyList<PlayerWindowSize> WindowSizes { get; } = Array.AsReadOnly(new[]
    {
        new PlayerWindowSize(960, 540), new PlayerWindowSize(1280, 720),
        new PlayerWindowSize(1600, 900), new PlayerWindowSize(1920, 1080)
    });
    public PlayerDisplayMode Mode => _window?.WindowState == WindowState.FullScreen ? PlayerDisplayMode.BorderlessFullscreen : PlayerDisplayMode.Window;
    public PlayerWindowSize WindowSize { get; private set; } = new(1280, 720);
    public event Action? Changed;

    public void Bind(Window? window)
    {
        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        _window = window;
        if (window is not null) window.PropertyChanged += OnWindowChanged;
        Changed?.Invoke();
    }

    public Task ApplyModeAsync(PlayerDisplayMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_window is null || !Modes.Contains(mode)) throw new InvalidOperationException("Display mode is unavailable.");
        _window.WindowState = mode == PlayerDisplayMode.Window ? WindowState.Normal : WindowState.FullScreen;
        if (mode == PlayerDisplayMode.Window) { _window.Width = WindowSize.Width; _window.Height = WindowSize.Height; }
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task ApplyWindowSizeAsync(PlayerWindowSize size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_window is null || Mode != PlayerDisplayMode.Window || !WindowSizes.Contains(size))
            throw new InvalidOperationException("Window size cannot be changed in this display mode.");
        _window.WindowState = WindowState.Normal;
        _window.Width = size.Width;
        _window.Height = size.Height;
        WindowSize = size;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) Changed?.Invoke();
    }
    public void Dispose() => Bind(null);
}
