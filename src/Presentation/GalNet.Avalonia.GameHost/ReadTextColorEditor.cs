using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Game.Controls.Forms;

namespace GalNet.Avalonia.GameHost;

internal static class ReadTextColorEditor
{
    public static FieldEditorLease Create(SettingItem<string> colorSetting, BoolSetting enabled, IPlayerLocalization localization)
    {
        var panel = new StackPanel { Name = "ReadTextColorEditor", Orientation = Orientation.Horizontal, Spacing = 16 };
        var check = new CheckBox { Name = "EnableReadTextColor" };
        check.Bind(ContentControl.ContentProperty, new Binding("Value") { Source = localization.GetString("Settings.EnableReadTextColor") });
        var swatch = new Border { Width = 28, Height = 20, BorderBrush = Brushes.Gray, BorderThickness = new(1) };
        var button = new Button { Name = "ReadTextColorPicker", Content = swatch };
        button.Bind(ToolTip.TipProperty, new Binding("Value") { Source = localization.GetString("Settings.ReadTextColor") });
        var picker = new ColorView { IsAlphaEnabled = false, IsAlphaVisible = false };
        picker.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.GameHost/"))
        { Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml") });
        var apply = new Button();
        apply.Bind(ContentControl.ContentProperty, new Binding("Value") { Source = localization.GetString("Common.Apply") });
        var cancel = new Button();
        cancel.Bind(ContentControl.ContentProperty, new Binding("Value") { Source = localization.GetString("Common.Cancel") });
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
        { picker, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, apply } } }
            }
        };
        button.Flyout = flyout;
        panel.Children.Add(check); panel.Children.Add(button);
        var refreshing = false;
        var disposed = false;
        void Refresh()
        {
            if (disposed) return;
            refreshing = true;
            check.IsChecked = enabled.Value;
            check.IsEnabled = enabled.IsEnabled;
            button.IsVisible = enabled.Value;
            if (!enabled.Value) flyout.Hide();
            button.IsEnabled = colorSetting.IsEnabled;
            swatch.Background = Color.TryParse(colorSetting.Value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
            refreshing = false;
        }
        async void Toggle(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (!refreshing && args.Property == ToggleButton.IsCheckedProperty)
            { await enabled.TrySetValueAsync(check.IsChecked == true); Refresh(); }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        check.PropertyChanged += Toggle;
        enabled.PropertyChanged += Changed; colorSetting.PropertyChanged += Changed;
        flyout.Opened += (_, _) => picker.Color = Color.TryParse(colorSetting.Value, out var color) ? color : Color.Parse("#A8B8C8");
        apply.Click += async (_, _) =>
        {
            if (disposed || !enabled.Value || !colorSetting.IsEnabled) return;
            var color = picker.Color;
            if (await colorSetting.TrySetValueAsync($"#{color.R:X2}{color.G:X2}{color.B:X2}")) flyout.Hide();
        };
        cancel.Click += (_, _) => flyout.Hide();
        Refresh();
        return new FieldEditorLease(panel, () =>
        {
            disposed = true; flyout.Hide();
            check.PropertyChanged -= Toggle;
            enabled.PropertyChanged -= Changed; colorSetting.PropertyChanged -= Changed;
        });
    }
}
