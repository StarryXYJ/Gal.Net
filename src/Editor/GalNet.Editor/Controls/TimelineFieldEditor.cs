using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Layout;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class TimelineFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, Action<FieldEditorContext>? open)
    {
        var panel = new StackPanel { Name = "TimelineProperty", Orientation = Orientation.Horizontal, Spacing = 8 };
        var button = new Button { Name = "OpenTimeline" };
        var summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var repair = PropertyEditorSupport.Repair(context);
        panel.Children.Add(button); panel.Children.Add(summary); panel.Children.Add(repair.Button);
        void Refresh()
        {
            button.Content = PropertyEditorSupport.Text("Form.OpenTimeline"); button.IsEnabled = context.IsEnabled && open is not null;
            repair.Button.IsVisible = false;
            try
            {
                if (context.Value.Json is not { } json || json.ValueKind == System.Text.Json.JsonValueKind.Null) summary.Text = PropertyEditorSupport.Text("Form.TimelineEmpty");
                else
                {
                    var document = new TimelineDocument(json);
                    summary.Text = EditorLocalizationService.Current?.Format("Form.TimelineSummary", document.Duration, document.FrameRate, document.Tracks.Count)
                        ?? $"{document.Duration} / {document.FrameRate} / {document.Tracks.Count}";
                }
            }
            catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or FormatException or OverflowException)
            { summary.Text = PropertyEditorSupport.Text("Timeline.Invalid"); repair.Button.IsVisible = true; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        button.Click += (_, _) => { if (context.IsEnabled) open?.Invoke(context); };
        context.PropertyChanged += Changed; var locale = PropertyEditorSupport.Localize(Refresh);
        return new(panel, () => { context.PropertyChanged -= Changed; locale.Dispose(); repair.Lifetime.Dispose(); });
    }
}
