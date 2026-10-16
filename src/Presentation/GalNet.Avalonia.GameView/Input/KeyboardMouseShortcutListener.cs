using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Page;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>
/// A nonvisual, scoped input subscription. Keyboard input belongs to the containing top level,
/// while pointer input belongs to the player surface; no hidden control needs to steal focus.
/// </summary>
public sealed class KeyboardMouseShortcutListener : IDisposable
{
    private readonly Control _root;
    private readonly PlayerInputSettings _settings;
    private readonly Func<bool> _canReceive;
    private readonly Func<string, Task> _dispatch;
    private readonly Action _releaseHold;
    private readonly HashSet<Key> _pressed = [];
    private TopLevel? _topLevel;
    private long _holdRequest;
    private bool _disposed;

    public KeyboardMouseShortcutListener(Control root, PlayerInputSettings settings, Func<bool> canReceive,
        Func<string, Task> dispatch, Action releaseHold)
    {
        _root = root; _settings = settings; _canReceive = canReceive; _dispatch = dispatch; _releaseHold = releaseHold;
        root.AttachedToVisualTree += Attached;
        root.DetachedFromVisualTree += Detached;
        root.LostFocus += LostFocus;
        root.AddHandler(InputElement.PointerPressedEvent, PointerDown, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.PointerReleasedEvent, PointerUp, RoutingStrategies.Tunnel);
        Attach();
    }

    private bool CanReceive => !_disposed && _topLevel is not null && _root.IsEffectivelyVisible &&
        _root.IsEffectivelyEnabled && !_settings.IsCapturing && _canReceive();

    private void Attached(object? sender, VisualTreeAttachmentEventArgs e) => Attach();
    private void Attach()
    {
        var topLevel = TopLevel.GetTopLevel(_root);
        if (_disposed || ReferenceEquals(topLevel, _topLevel)) return;
        Detach();
        _topLevel = topLevel;
        if (topLevel is null) return;
        topLevel.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.KeyUpEvent, KeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (topLevel is Window window) window.Deactivated += Deactivated;
    }

    private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => Detach();
    private void Detach()
    {
        Reset();
        if (_topLevel is not { } topLevel) return;
        topLevel.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        topLevel.RemoveHandler(InputElement.KeyUpEvent, KeyUp);
        if (topLevel is Window window) window.Deactivated -= Deactivated;
        _topLevel = null;
    }

    private bool AcceptKeyboard(object? source, Key key)
    {
        // A window can own keyboard focus before the player has been clicked.
        if (source is null || ReferenceEquals(source, _topLevel)) return true;
        if (source is not Visual visual) return false;
        var ancestors = visual.GetSelfAndVisualAncestors().ToArray();
        if (!ancestors.Contains(_root)) return false;
        if (ancestors.Any(item => item is TextBox or SelectingItemsControl or RangeBase or MenuBase)) return false;
        // Keep native button activation, but let H/A/Tab/etc. work after clicking the toolbar.
        return !ancestors.Any(item => item is Button || item is Control control && GamePage.GetIsPlayerInteraction(control)) ||
            key is not (Key.Enter or Key.Space);
    }

    private static bool IsPointerInteraction(object? source) => (source as Visual)?.GetSelfAndVisualAncestors()
        .Any(item => item is Button or TextBox or SelectingItemsControl or RangeBase ||
            item is Control control && GamePage.GetIsPlayerInteraction(control)) == true;

    private async void KeyDown(object? sender, KeyEventArgs e)
    {
        _settings.LastDevice = PlayerInputDevice.KeyboardMouse;
        if (!CanReceive || !AcceptKeyboard(e.Source, e.Key)) { Reset(); return; }
        if (!_pressed.Add(e.Key)) return;
        var action = _settings.Resolve(PlayerInputDevice.KeyboardMouse,
            InputGestureCodec.Compose(InputGestureCodec.KeyCode(e.Key), e.KeyModifiers));
        CancelHold();
        if (action is null) return;
        e.Handled = true;
        if (action == "hold.quick")
        {
            // Give a modifier time to become a chord (e.g. Ctrl+S) without starting quick mode.
            var request = _holdRequest;
            await Task.Delay(160);
            if (request != _holdRequest || !_pressed.Contains(e.Key) ||
                !CanReceive || !AcceptKeyboard(_topLevel?.FocusManager?.GetFocusedElement(), e.Key)) return;
        }
        await _dispatch(action);
    }

    private void KeyUp(object? sender, KeyEventArgs e)
    { _pressed.Remove(e.Key); CancelHold(); }

    private async void PointerDown(object? sender, PointerPressedEventArgs e)
    {
        _settings.LastDevice = e.Pointer.Type == PointerType.Touch ? PlayerInputDevice.Touch : PlayerInputDevice.KeyboardMouse;
        if (e.Pointer.Type == PointerType.Touch || !CanReceive || IsPointerInteraction(e.Source)) return;
        var code = InputGestureCodec.MouseCode(e.GetCurrentPoint(_root).Properties.PointerUpdateKind);
        if (code is not null) await EmitPointer(e, InputGestureCodec.Compose(code, e.KeyModifiers));
    }

    private void PointerUp(object? sender, PointerReleasedEventArgs e) => CancelHold();
    private async void Wheel(object? sender, PointerWheelEventArgs e)
    {
        _settings.LastDevice = PlayerInputDevice.KeyboardMouse;
        if (!CanReceive || IsPointerInteraction(e.Source) || e.Delta.Y == 0) return;
        await EmitPointer(e, InputGestureCodec.Compose(e.Delta.Y > 0 ? "WheelUp" : "WheelDown", e.KeyModifiers));
    }

    private async Task EmitPointer(RoutedEventArgs e, string gesture)
    {
        CancelHold();
        if (_settings.Resolve(PlayerInputDevice.KeyboardMouse, gesture) is not { } action) return;
        e.Handled = true;
        await _dispatch(action);
    }

    private void CancelHold() { ++_holdRequest; _releaseHold(); }
    public void Reset() { _pressed.Clear(); CancelHold(); }
    private void LostFocus(object? sender, RoutedEventArgs e) => Reset();
    private void Deactivated(object? sender, EventArgs e) => Reset();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
        _root.AttachedToVisualTree -= Attached;
        _root.DetachedFromVisualTree -= Detached;
        _root.LostFocus -= LostFocus;
        _root.RemoveHandler(InputElement.PointerPressedEvent, PointerDown);
        _root.RemoveHandler(InputElement.PointerWheelChangedEvent, Wheel);
        _root.RemoveHandler(InputElement.PointerReleasedEvent, PointerUp);
    }
}
