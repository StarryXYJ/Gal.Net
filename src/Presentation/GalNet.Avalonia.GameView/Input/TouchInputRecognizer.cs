using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Arbitrates one gesture per contact sequence and consumes touch before compatibility clicks.</summary>
public sealed class TouchInputRecognizer : IDisposable
{
    private readonly Control _root;
    private readonly PlayerInputSettings _settings;
    private readonly Func<bool> _canReceive;
    private readonly Func<string, Task> _dispatch;
    private readonly Dictionary<int, Point> _contacts = [];
    private readonly DispatcherTimer _doubleTap = new() { Interval = TimeSpan.FromMilliseconds(280) };
    private Point _start;
    private Point _end;
    private long _started;
    private int _maxContacts;
    private bool _tapPending;
    public TouchInputRecognizer(Control root, PlayerInputSettings settings, Func<bool> canReceive, Func<string, Task> dispatch)
    {
        _root = root; _settings = settings; _canReceive = canReceive; _dispatch = dispatch;
        root.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        _doubleTap.Tick += TapElapsed;
    }
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch) return;
        _settings.LastDevice = PlayerInputDevice.Touch;
        if (!_canReceive() || _settings.IsCapturing || (e.Source as Visual)?.GetSelfAndVisualAncestors().Any(item => item is Button or SelectingItemsControl or TextBox) == true) return;
        e.Handled = true;
        var point = e.GetPosition(_root);
        if (_contacts.Count == 0)
        { _start = _end = point; _started = Environment.TickCount64; _maxContacts = 0; }
        _contacts[e.Pointer.Id] = point;
        _maxContacts = Math.Max(_maxContacts, _contacts.Count);
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (!_contacts.ContainsKey(e.Pointer.Id)) return;
        e.Handled = true;
        _end = e.GetPosition(_root);
    }
    private async void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (!_contacts.Remove(e.Pointer.Id)) return;
        e.Handled = true;
        if (_contacts.Count > 0) return;
        _end = e.GetPosition(_root);
        if (!_canReceive()) { Reset(); return; }
        var delta = _end - _start;
        var longPress = Environment.TickCount64 - _started >= 550;
        string gesture;
        if (_maxContacts >= 2) gesture = longPress ? "TwoFingerLongPress" : "TwoFingerTap";
        else if (Math.Abs(delta.X) > 60 || Math.Abs(delta.Y) > 60)
            gesture = Math.Abs(delta.X) >= Math.Abs(delta.Y) ? delta.X > 0 ? "SwipeRight" : "SwipeLeft" : delta.Y > 0 ? "SwipeDown" : "SwipeUp";
        else gesture = longPress ? "LongPress" : "Tap";
        if (gesture == "Tap" && _settings.Resolve(PlayerInputDevice.Touch, "DoubleTap") is not null)
        {
            if (_tapPending) { _tapPending = false; _doubleTap.Stop(); await Emit("DoubleTap"); }
            else { _tapPending = true; _doubleTap.Start(); }
        }
        else
        { _tapPending = false; _doubleTap.Stop(); await Emit(gesture); }
    }
    private async void TapElapsed(object? sender, EventArgs e)
    { _doubleTap.Stop(); if (!_tapPending) return; _tapPending = false; await Emit("Tap"); }
    private Task Emit(string gesture) => _canReceive() && _settings.Resolve(PlayerInputDevice.Touch, gesture) is { } action ? _dispatch(action) : Task.CompletedTask;
    public void Reset() { _contacts.Clear(); _doubleTap.Stop(); _tapPending = false; }
    public void Dispose()
    {
        Reset();
        _doubleTap.Tick -= TapElapsed;
        _root.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
        _root.RemoveHandler(InputElement.PointerMovedEvent, Moved);
        _root.RemoveHandler(InputElement.PointerReleasedEvent, Released);
    }
}
