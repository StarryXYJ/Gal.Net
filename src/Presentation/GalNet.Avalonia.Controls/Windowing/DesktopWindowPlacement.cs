using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;

namespace GalNet.Game.Controls.Windowing;

public sealed record WindowPlacement(double Width, double Height, bool Maximized);

/// <summary>Desktop geometry in DIPs. Positions are deliberately not persisted.</summary>
public sealed class DesktopWindowPlacement
{
    private static readonly ConditionalWeakTable<Window, DesktopWindowPlacement> Placements = new();
    private readonly Window _window;
    private readonly Action<WindowPlacement> _save;
    private readonly WindowPlacement? _saved;
    private Size _normalSize;
    private bool _maximized;
    private bool _restoring;
    private bool _displayRestored;

    public DesktopWindowPlacement(Window window, WindowPlacement? saved, Action<WindowPlacement> save)
    {
        _window = window;
        _saved = saved;
        _save = save;
        _normalSize = new Size(window.Width, window.Height);
        Placements.Add(window, this);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (window.IsVisible) Restore(saved);
        else
        {
            PrepareStartupPlacement(saved);
            window.Opened += OnOpened;
        }
        window.SizeChanged += OnSizeChanged;
        window.PropertyChanged += OnPropertyChanged;
        window.Closed += OnClosed;
    }

    public static Size? GetNormalSize(Window window) => Placements.TryGetValue(window, out var placement) ? placement._normalSize : null;

    // Placement wins over legacy preset sizes; legacy sizes remain the first-run fallback.
    public static bool RestoreDisplayPreferences(Window window, Size fallback)
    {
        if (Placements.TryGetValue(window, out var placement))
        {
            var saved = placement._displayRestored
                ? new WindowPlacement(placement._normalSize.Width, placement._normalSize.Height, placement._maximized)
                : placement._saved ?? new WindowPlacement(fallback.Width, fallback.Height, false);
            placement._displayRestored = true;
            placement.Restore(saved);
            return true;
        }
        return false;
    }

    public static Size Fit(Size desired, Size minimum, Size available)
    {
        var limit = new Size(Math.Max(1, available.Width * .95), Math.Max(1, available.Height * .95));
        var width = double.IsFinite(desired.Width) && desired.Width > 0 ? desired.Width : 1200;
        var height = double.IsFinite(desired.Height) && desired.Height > 0 ? desired.Height : 800;
        width = Math.Max(width, Math.Min(minimum.Width, limit.Width));
        height = Math.Max(height, Math.Min(minimum.Height, limit.Height));
        var scale = Math.Min(1, Math.Min(limit.Width / width, limit.Height / height));
        return new Size(width * scale, height * scale);
    }

    private void OnOpened(object? sender, EventArgs args) => Restore(_saved);

    private void Restore(WindowPlacement? saved)
    {
        var alreadyMaximized = saved?.Maximized == true && _window.WindowState == WindowState.Maximized;
        _restoring = true;
        try
        {
            var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
            var desired = saved is null ? _normalSize : new Size(saved.Width, saved.Height);
            if (!alreadyMaximized) _window.WindowState = WindowState.Normal;
            var frame = _window.FrameSize ?? _window.ClientSize;
            var border = new Size(Math.Max(0, frame.Width - _window.ClientSize.Width), Math.Max(0, frame.Height - _window.ClientSize.Height));
            var size = screen is null ? desired : Fit(desired, new Size(_window.MinWidth, _window.MinHeight),
                new Size(screen.WorkingArea.Width / screen.Scaling - border.Width, screen.WorkingArea.Height / screen.Scaling - border.Height));
            _window.MinWidth = Math.Min(_window.MinWidth, size.Width);
            _window.MinHeight = Math.Min(_window.MinHeight, size.Height);
            _window.Width = size.Width;
            _window.Height = size.Height;
            _normalSize = size;
            if (screen is not null && !alreadyMaximized)
            {
                var area = screen.WorkingArea;
                _window.Position = new PixelPoint(area.X + (int)((area.Width - (size.Width + border.Width) * screen.Scaling) / 2),
                    area.Y + (int)((area.Height - (size.Height + border.Height) * screen.Scaling) / 2));
            }
            _maximized = saved?.Maximized == true;
            if (_maximized && !alreadyMaximized) _window.WindowState = WindowState.Maximized;
        }
        finally { _restoring = false; }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (!_restoring && _window.WindowState == WindowState.Normal && args.NewSize.Width > 0 && args.NewSize.Height > 0)
            _normalSize = _window.ClientSize;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (!_restoring && args.Property == Window.WindowStateProperty)
        {
            if (_window.WindowState == WindowState.Maximized) _maximized = true;
            else if (_window.WindowState == WindowState.Normal) _maximized = false;
        }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _window.Opened -= OnOpened;
        _window.SizeChanged -= OnSizeChanged;
        _window.PropertyChanged -= OnPropertyChanged;
        _window.Closed -= OnClosed;
        Placements.Remove(_window);
        _save(new WindowPlacement(_normalSize.Width, _normalSize.Height, _maximized));
    }

    public static WindowPlacement? ReadFile(string path)
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(path), WindowPlacementJsonContext.Default.WindowPlacement); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static void WriteFile(string path, WindowPlacement placement)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(placement, WindowPlacementJsonContext.Default.WindowPlacement));
        File.Move(temporary, path, overwrite: true);
    }

    private void PrepareStartupPlacement(WindowPlacement? saved)
    {
        if (saved is null || !double.IsFinite(saved.Width) || saved.Width <= 0 ||
            !double.IsFinite(saved.Height) || saved.Height <= 0) return;

        _restoring = true;
        try
        {
            _window.Width = saved.Width;
            _window.Height = saved.Height;
            _normalSize = new Size(saved.Width, saved.Height);
            _maximized = saved.Maximized;
            if (_maximized) _window.WindowState = WindowState.Maximized;
        }
        finally { _restoring = false; }
    }
}

[JsonSerializable(typeof(WindowPlacement))]
internal partial class WindowPlacementJsonContext : JsonSerializerContext;
