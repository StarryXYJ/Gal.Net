using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.MarkupExtensions;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>A leased settings editor; player values and persistence belong to PlayerInputSettings.</summary>
public sealed class InputBindingsPanel : UserControl, IDisposable
{
    private readonly PlayerInputSettings _settings;
    private readonly TabControl _tabs = new();
    private readonly List<Action> _refresh = [];
    private readonly List<InputBindingCapture> _captures = [];
    private bool _refreshing;
    private bool _disposed;

    public InputBindingsPanel(PlayerInputSettings settings)
    {
        _settings = settings;
        var tabs = new List<TabItem>();
        foreach (var device in Enum.GetValues<PlayerInputDevice>())
        {
            var rows = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 0) };
            var supported = device != PlayerInputDevice.Gamepad || settings.GamepadSupported;
            if (device == PlayerInputDevice.Touch) supported = settings.TouchSupported;
            if (device == PlayerInputDevice.Gamepad)
            {
                var layout = new ComboBox { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
                layout.ItemsSource = new Control[] { Text("Input.SouthConfirms"), Text("Input.EastConfirms") };
                _refresh.Add(() => layout.SelectedIndex = settings.EastConfirms ? 1 : 0);
                layout.SelectionChanged += async (_, _) =>
                {
                    if (_settings.IsPreviewingChoice || _refreshing) return;
                    if (layout.SelectedIndex >= 0)
                        await settings.SetLayoutAsync(layout.SelectedIndex == 1);
                    Refresh();
                };
                rows.Children.Add(Text("Input.ConfirmLayout"));
                rows.Children.Add(layout);
            }
            if (!supported) rows.Children.Add(Text("Input.DeviceUnavailable"));
            foreach (var action in PlayerInputSettings.Actions)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,*,Auto"), ColumnSpacing = 12 };
                row.Children.Add(Text(action.NameKey));
                var available = supported && settings.IsActionAvailable(action.Id) && !(device == PlayerInputDevice.Touch && action.IsHold);
                for (var index = 0; index < 2; index++)
                {
                    var slot = index;
                    Control editor;
                    if (device == PlayerInputDevice.Touch)
                    {
                        var combo = new ComboBox { HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch, IsEnabled = available };
                        combo.ItemsSource = new[] { "None" }.Concat(PlayerInputSettings.TouchGestures).Select(gesture => Text("Input.Touch." + gesture)).ToArray();
                        _refresh.Add(() =>
                        {
                            var current = settings.GetBindings(action.Id, device).ElementAtOrDefault(slot)?.Gesture;
                            combo.SelectedIndex = string.IsNullOrEmpty(current) ? 0 : PlayerInputSettings.TouchGestures.ToList().IndexOf(current) + 1;
                        });
                        combo.SelectionChanged += async (_, _) =>
                        {
                            if (_refreshing || settings.IsPreviewingChoice || combo.SelectedIndex < 0) return;
                            await settings.SetBindingAsync(action.Id, device, slot, combo.SelectedIndex == 0 ? null : PlayerInputSettings.TouchGestures[combo.SelectedIndex - 1]);
                            Refresh();
                        };
                        editor = combo;
                    }
                    else
                    {
                        var capture = new InputBindingCapture(settings, action.Id, device, slot) { IsEnabled = available };
                        _captures.Add(capture);
                        _refresh.Add(capture.Refresh);
                        editor = capture;
                    }
                    Grid.SetColumn(editor, slot + 1);
                    row.Children.Add(editor);
                }
                var clear = new Button { Content = Text("Input.Clear"), IsEnabled = available };
                clear.Click += async (_, _) =>
                {
                    await settings.SetBindingAsync(action.Id, device, 1, null);
                    await settings.SetBindingAsync(action.Id, device, 0, null);
                    Refresh();
                };
                Grid.SetColumn(clear, 3);
                row.Children.Add(clear);
                rows.Children.Add(new Border { Classes = { "player-setting-row" }, Child = row });
                if (!settings.IsActionAvailable(action.Id)) rows.Children.Add(Text("Input.HistoryUnavailable"));
            }
            var reset = new Button { Content = Text("Input.ResetDevice") };
            reset.Click += async (_, _) => { await settings.ResetAsync(device); Refresh(); };
            rows.Children.Add(reset);
            tabs.Add(new TabItem { Header = Text("Input.Device." + device), Content = rows });
        }
        _tabs.ItemsSource = tabs;
        _tabs.SelectedIndex = (int)(settings.SelectedDevice ?? settings.LastDevice);
        _tabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, _tabs) && _tabs.SelectedIndex >= 0)
                settings.SelectedDevice = (PlayerInputDevice)_tabs.SelectedIndex;
        };
        var error = Text(null);
        var conflict = Text(null);
        _refresh.Add(() => error.Key = settings.ErrorKey);
        _refresh.Add(() => conflict.Key = settings.ConflictActionKey);
        Content = new StackPanel { Spacing = 12, Children = { _tabs, error, conflict } };
        settings.Changed += Refresh;
        Refresh();
    }
    private static PlayerTextBlock Text(string? key) => new() { Key = key, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
    private void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        try { foreach (var refresh in _refresh) refresh(); }
        finally { _refreshing = false; }
    }
    public void Dispose()
    {
        _disposed = true;
        _settings.Changed -= Refresh;
        foreach (var capture in _captures) capture.Dispose();
    }
}

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
    public InputBindingCapture(PlayerInputSettings settings, string action, PlayerInputDevice device, int slot)
    { _settings = settings; _action = action; _device = device; _slot = slot; Refresh(); }
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
    private bool _gamepadArmed;
    private int _gamepadPeak;
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
