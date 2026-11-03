using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class EditorSettingsPanelView : UserControl
{
    private TopLevel? _captureRoot;
    private Button? _captureButton;
    private ShortcutSettingItemViewModel? _captureItem;

    public EditorSettingsPanelView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => CancelCapture();
    }

    private void OnCaptureShortcut(object? sender, RoutedEventArgs args)
    {
        CancelCapture();
        if (sender is not Button { DataContext: ShortcutSettingItemViewModel item } button ||
            TopLevel.GetTopLevel(button) is not { } root || DataContext is not EditorSettingsPanelViewModel vm) return;
        _captureRoot = root; _captureButton = button; _captureItem = item;
        button.SetCurrentValue(ContentControl.ContentProperty, vm.L["Settings.Shortcuts.Capture"]);
        root.AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
        root.AddHandler(PointerPressedEvent, OnCapturePointerDown, RoutingStrategies.Tunnel);
        if (root is Window window) window.Deactivated += OnCaptureWindowDeactivated;
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs args)
    {
        args.Handled = true;
        if (args.Key == Key.Escape) { CancelCapture(); return; }
        if (args.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        var item = _captureItem;
        var value = args.Key == Key.Back && args.KeyModifiers == KeyModifiers.None ? "" : new KeyGesture(args.Key, args.KeyModifiers).ToString();
        CancelCapture();
        item?.ApplyGesture(value);
    }

    private void OnCapturePointerDown(object? sender, PointerPressedEventArgs args) => CancelCapture();
    private void OnCaptureWindowDeactivated(object? sender, EventArgs args) => CancelCapture();

    private void CancelCapture()
    {
        if (_captureRoot is not null)
        {
            _captureRoot.RemoveHandler(KeyDownEvent, OnCaptureKeyDown);
            _captureRoot.RemoveHandler(PointerPressedEvent, OnCapturePointerDown);
            if (_captureRoot is Window window) window.Deactivated -= OnCaptureWindowDeactivated;
        }
        if (_captureButton is not null && _captureItem is not null)
            _captureButton.SetCurrentValue(ContentControl.ContentProperty, _captureItem.GestureText);
        _captureRoot = null; _captureButton = null; _captureItem = null;
    }

    private async void OnResetAll(object? sender, RoutedEventArgs args)
    {
        CancelCapture();
        if (TopLevel.GetTopLevel(this) is not Window owner || DataContext is not EditorSettingsPanelViewModel vm) return;
        var dialog = new Window { Title = vm.L["Settings.Shortcuts.ResetAll"], Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = vm.L["Common.Cancel"] };
        var confirm = new Button { Content = vm.L["Common.Reset"] };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20, Children = { new TextBlock { Text = vm.L["Settings.Shortcuts.ResetAllQuestion"], TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons } };
        if (await dialog.ShowDialog<bool>(owner)) vm.ResetAllShortcutsCommand.Execute(null);
    }
}
