using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class ColorFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context) => Create(context, false);
    public static FieldEditorLease Create(FieldEditorContext context, bool rgba)
    {
        var panel = new Grid { Name = "ColorEditor", ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8 };
        var swatch = new Border { Width = 24, Height = 18, BorderThickness = new(1), BorderBrush = Brushes.Gray };
        var button = new Button { Name = "PickColor", Content = swatch };
        var input = new TextBox { Name = "ColorHex", PlaceholderText = "#AARRGGBB", MinWidth = 90 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(input, 1); Grid.SetRow(error, 1); Grid.SetColumn(error, 1);
        panel.Children.Add(button); panel.Children.Add(input); panel.Children.Add(error);
        var picker = new ColorView { Name = "PropertyColorPicker", IsAlphaEnabled = true, IsAlphaVisible = true };
        var apply = new Button { Name = "ApplyColor" }; var cancel = new Button { Name = "CancelColor" };
        var popupButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        popupButtons.Children.Add(apply); popupButtons.Children.Add(cancel);
        var popup = new StackPanel { Spacing = 8, Margin = new(10), Width = 330 }; popup.Children.Add(picker); popup.Children.Add(popupButtons);
        var flyout = new Flyout { Content = popup }; button.Flyout = flyout;
        var syncing = false; var opened = false; var disposed = false; var ownCommit = false; var baseline = context.Value;
        void ShowError(string? message) { error.Text = message; error.IsVisible = !string.IsNullOrEmpty(message); }
        void Refresh()
        {
            if (disposed) return;
            if (opened && (!context.IsEnabled || !Same(baseline, context.Value)) && !ownCommit) flyout.Hide();
            syncing = true;
            try
            {
                input.Text = context.Text;
                var valid = TryHex(context.Text, rgba, out var color);
                swatch.Background = valid ? new SolidColorBrush(color) : Brushes.Transparent;
                panel.IsEnabled = context.IsEnabled;
                ShowError(context.Text.Length > 0 && !valid ? PropertyEditorSupport.Text("Form.ColorInvalid") : context.Issue?.Message);
            }
            finally { syncing = false; }
        }
        async Task CommitText()
        {
            if (disposed || opened || !context.HasDraft) return;
            if (context.Text.Length == 0 && context.CanOmit) { await context.OmitAsync(); return; }
            if (!TryHex(context.Text, rgba, out var color)) { ShowError(PropertyEditorSupport.Text("Form.ColorInvalid")); return; }
            await context.CommitValueAsync(Hex(color, rgba)); Refresh();
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        input.TextChanged += (_, _) => { if (!syncing) { context.Text = input.Text ?? ""; } };
        input.LostFocus += async (_, _) => await CommitText();
        input.AddHandler(InputElement.KeyDownEvent, async (_, args) =>
        {
            if (args.Key == Key.Escape) { context.RevertDraft(); Refresh(); args.Handled = true; }
            else if (args.Key == Key.Enter) { await CommitText(); args.Handled = true; }
        }, RoutingStrategies.Tunnel);
        flyout.Opened += (_, _) =>
        {
            context.RevertDraft(); baseline = context.Value; opened = true;
            picker.Color = TryHex(context.Text, rgba, out var color) ? color : Colors.Transparent;
        };
        flyout.Closed += (_, _) => { opened = false; Refresh(); };
        apply.Click += async (_, _) =>
        {
            if (!opened || disposed || !context.IsEnabled || !Same(baseline, context.Value)) { flyout.Hide(); return; }
            ownCommit = true;
            try { if (await context.CommitValueAsync(Hex(picker.Color, rgba))) flyout.Hide(); }
            finally { ownCommit = false; Refresh(); }
        };
        cancel.Click += (_, _) => flyout.Hide();
        var locale = PropertyEditorSupport.Localize(() =>
        {
            apply.Content = PropertyEditorSupport.Text("Common.Apply"); cancel.Content = PropertyEditorSupport.Text("Common.Cancel");
            var name = PropertyEditorSupport.Text("Form.PickColor"); ToolTip.SetTip(button, name);
            global::Avalonia.Automation.AutomationProperties.SetName(button, name); Refresh();
        });
        context.PropertyChanged += Changed; Refresh();
        return new(panel, () => { disposed = true; flyout.Hide(); context.PropertyChanged -= Changed; locale.Dispose(); });
    }
    private static bool Same(FieldValue a, FieldValue b) => a.IsProvided == b.IsProvided && (!a.IsProvided || JsonElement.DeepEquals(a.Json!.Value, b.Json!.Value));
    private static bool TryHex(string text, bool rgba, out Color color)
    {
        color = default;
        if (text.Length is not (7 or 9) || text[0] != '#' || !uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return false;
        var value = rgba && text.Length == 9 ? "#" + text[7..9] + text[1..7] : text;
        return Color.TryParse(value, out color);
    }
    private static string Hex(Color color, bool rgba) => color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : rgba ? $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
