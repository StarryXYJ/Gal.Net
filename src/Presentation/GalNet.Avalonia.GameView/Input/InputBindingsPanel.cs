using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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
            bool Supported() => device switch
            {
                PlayerInputDevice.Gamepad => settings.GamepadSupported,
                PlayerInputDevice.Touch => settings.TouchSupported, _ => true
            };
            if (device == PlayerInputDevice.Gamepad)
            {
                var layout = Choice(["Input.SouthConfirms", "Input.EastConfirms"]);
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
            var unavailable = Text("Input.DeviceUnavailable");
            rows.Children.Add(unavailable);
            _refresh.Add(() => unavailable.IsVisible = !Supported());
            foreach (var action in PlayerInputSettings.Actions)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*,*,Auto"), ColumnSpacing = 12 };
                row.Children.Add(Text(action.NameKey));
                bool Available() => Supported() && settings.IsActionAvailable(action.Id) && !(device == PlayerInputDevice.Touch && action.IsHold);
                for (var index = 0; index < 2; index++)
                {
                    var slot = index;
                    Control editor;
                    if (device == PlayerInputDevice.Touch)
                    {
                        var combo = Choice(new[] { "None" }.Concat(PlayerInputSettings.TouchGestures).Select(gesture => "Input.Touch." + gesture));
                        _refresh.Add(() =>
                        {
                            combo.IsEnabled = Available();
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
                        var capture = new InputBindingCapture(settings, action.Id, device, slot);
                        _refresh.Add(() => capture.IsEnabled = Available());
                        _captures.Add(capture);
                        _refresh.Add(capture.Refresh);
                        editor = capture;
                    }
                    Grid.SetColumn(editor, slot + 1);
                    row.Children.Add(editor);
                }
                var clear = new Button { Content = Text("Input.Clear") };
                _refresh.Add(() => clear.IsEnabled = Available());
                clear.Click += async (_, _) =>
                {
                    await settings.SetBindingAsync(action.Id, device, 1, null);
                    await settings.SetBindingAsync(action.Id, device, 0, null);
                    Refresh();
                };
                Grid.SetColumn(clear, 3);
                row.Children.Add(clear);
                rows.Children.Add(row);
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
    private static ComboBox Choice(IEnumerable<string> keys) => new()
    {
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
        ItemsSource = keys.ToArray(),
        ItemTemplate = new FuncDataTemplate<string>((key, _) => Text(key))
    };
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
