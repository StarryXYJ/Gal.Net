using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class AudioBusFieldEditor
{
    private sealed record Option(string? Id, string Label) { public override string ToString() => Label; }

    public static FieldEditorLease Create(FieldEditorContext context)
    {
        if (context.HostContext is not ConfigurationSectionEditor section ||
            context.EffectiveValue is { ValueKind: not (JsonValueKind.String or JsonValueKind.Null) })
            return DefaultFieldEditors.CreateJson(context);
        var input = new ComboBox { Name = "AudioBusInput", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
        var disposed = false; var updating = false; var busy = false;
        void Refresh()
        {
            if (disposed) return;
            updating = true;
            try
            {
                var current = context.EffectiveValue is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
                var rows = new List<Option> { new(null, PropertyEditorSupport.Text("AssetPicker.None")) };
                rows.AddRange(section.AudioBuses().Select(bus => new Option(bus.Id, string.IsNullOrWhiteSpace(bus.Name) ? bus.Id : $"{bus.Name} ({bus.Id})")));
                if (current is not null && rows.All(row => row.Id != current)) rows.Add(new(current, current));
                if (input.ItemsSource is not List<Option> existing || !existing.SequenceEqual(rows)) input.ItemsSource = rows;
                input.SelectedItem = input.Items.Cast<Option>().FirstOrDefault(row => row.Id == current);
                input.IsEnabled = context.IsEnabled && !busy;
            }
            finally { updating = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        input.SelectionChanged += async (_, _) =>
        {
            if (updating || disposed || busy || !context.IsEnabled || input.SelectedItem is not Option selected) return;
            busy = true;
            try { await context.CommitAsync(new(JsonSerializer.SerializeToElement(selected.Id))); }
            finally { busy = false; Refresh(); }
        };
        context.PropertyChanged += Changed; section.AudioBusesChanged += Refresh;
        var locale = PropertyEditorSupport.Localize(Refresh); Refresh();
        return new(input, () => { disposed = true; context.PropertyChanged -= Changed; section.AudioBusesChanged -= Refresh; locale.Dispose(); });
    }
}
