using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.MarkupExtensions;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>One keyboard/mouse entry point, with an isolated draft until release and commit.</summary>
public sealed class InputBindingCapture : Button, IDisposable
{
    private readonly PlayerInputSettings _settings;
    private readonly string _action;
    private readonly PlayerInputDevice _device;
    private readonly int _slot;
    private TopLevel? _root;
    private string? _draft;
    private bool _capturing;
    private bool _gamepadArmed;
    private int _gamepadPeak;

    public InputBindingCapture(PlayerInputSettings settings, string action, PlayerInputDevice device, int slot)
    {
        _settings = settings;
        _action = action;
        _device = device;
        _slot = slot;
        Classes.Add("player-input-binding");
        Refresh();
    }

    protected override Type StyleKeyOverride => typeof(Button);
    protected override void OnClick()
    {
        if (_capturing || _settings.IsCapturing) return;
        _root = TopLevel.GetTopLevel(this);
        if (_root is null) return;
        _capturing = _settings.IsCapturing = true;
        _draft = null;
        _root.AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
        _root.AddHandler(KeyUpEvent, OnCaptureKeyUp, RoutingStrategies.Tunnel);
        _root.AddHandler(PointerPressedEvent, OnCapturePointerDown, RoutingStrategies.Tunnel);
        _root.AddHandler(PointerReleasedEvent, OnCapturePointerUp, RoutingStrategies.Tunnel);
        _root.AddHandler(PointerWheelChangedEvent, OnCaptureWheel, RoutingStrategies.Tunnel);
        _settings.GamepadChanged += OnGamepad;
        _settings.GamepadCancelled += Cancel;
        _gamepadArmed = _settings.GamepadButtons.Count == 0;
        Content = new PlayerTextBlock { Key = "Input.PressBinding" };
    }
    public void BeginCapture() => OnClick();
    private async void OnGamepad(IReadOnlySet<string> buttons)
    {
        if (_device != PlayerInputDevice.Gamepad) return;
        if (!_gamepadArmed) { if (buttons.Count == 0) _gamepadArmed = true; return; }
        if (buttons.Contains(_settings.EastConfirms ? "South" : "East")) { Cancel(); return; }
        if (buttons.Count > 0)
        {
            if (buttons.Count >= _gamepadPeak)
            { _gamepadPeak = buttons.Count; _draft = string.Join('+', buttons.Order(StringComparer.Ordinal)); Content = _draft; }
        }
        else if (_draft is not null) await FinishAsync();
    }
    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Escape) { Cancel(); return; }
        if (_device != PlayerInputDevice.KeyboardMouse) return;
        _draft = InputGestureCodec.Compose(InputGestureCodec.KeyCode(e.Key), e.KeyModifiers);
        Content = _draft;
    }
    private async void OnCaptureKeyUp(object? sender, KeyEventArgs e)
    { e.Handled = true; if (_device == PlayerInputDevice.KeyboardMouse && _draft is not null) await FinishAsync(); }
    private void OnCapturePointerDown(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (_device == PlayerInputDevice.KeyboardMouse && InputGestureCodec.MouseCode(e.GetCurrentPoint(this).Properties.PointerUpdateKind) is { } code)
        { _draft = InputGestureCodec.Compose(code, e.KeyModifiers); Content = _draft; }
    }
    private async void OnCapturePointerUp(object? sender, PointerReleasedEventArgs e)
    { e.Handled = true; if (_device == PlayerInputDevice.KeyboardMouse && _draft is not null) await FinishAsync(); }
    private async void OnCaptureWheel(object? sender, PointerWheelEventArgs e)
    {
        e.Handled = true;
        if (_device != PlayerInputDevice.KeyboardMouse || e.Delta.Y == 0) return;
        _draft = InputGestureCodec.Compose(e.Delta.Y > 0 ? "WheelUp" : "WheelDown", e.KeyModifiers);
        await FinishAsync();
    }
    private async Task FinishAsync()
    {
        var gesture = _draft;
        Cancel();
        await _settings.SetBindingAsync(_action, _device, _slot, gesture);
        Refresh();
    }
    public void Refresh()
    {
        if (_capturing) return;
        var gesture = _settings.GetBindings(_action, _device).ElementAtOrDefault(_slot)?.Gesture;
        Content = string.IsNullOrEmpty(gesture) ? new PlayerTextBlock { Key = "Input.Unbound" } : gesture;
    }
    public void Cancel()
    {
        _settings.GamepadChanged -= OnGamepad;
        _settings.GamepadCancelled -= Cancel;
        _gamepadArmed = false;
        _gamepadPeak = 0;
        if (_root is not null)
        {
            _root.RemoveHandler(KeyDownEvent, OnCaptureKeyDown);
            _root.RemoveHandler(KeyUpEvent, OnCaptureKeyUp);
            _root.RemoveHandler(PointerPressedEvent, OnCapturePointerDown);
            _root.RemoveHandler(PointerReleasedEvent, OnCapturePointerUp);
            _root.RemoveHandler(PointerWheelChangedEvent, OnCaptureWheel);
        }
        if (_capturing) _settings.IsCapturing = false;
        _capturing = false;
        _draft = null;
        _root = null;
        Refresh();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Cancel(); base.OnDetachedFromVisualTree(e); }
    public void Dispose() => Cancel();
}
