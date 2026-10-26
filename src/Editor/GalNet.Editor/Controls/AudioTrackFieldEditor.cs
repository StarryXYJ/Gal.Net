using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class AudioTrackFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, IProjectContentSession? content)
    {
        var input = new ComboBox { Name = "AudioTrackInput", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
        var disposed = false; var updating = false; var busy = false;
        void Refresh()
        {
            if (disposed) return;
            updating = true;
            try
            {
                var tracks = content is null ? [] : content.CaptureWorking().Configuration.ReadDefaultOptions().AudioTracks;
                var rows = tracks.Select(track => new TrackOption(track.Id, string.IsNullOrEmpty(track.Name) ? track.Id : $"{track.Name} ({track.Id})")).ToList();
                var value = context.EffectiveValue;
                var id = value is { ValueKind: JsonValueKind.String } json ? json.GetString() : null;
                // Keep unresolved author IDs visible without selecting a replacement on refresh.
                if (id is not null && rows.All(row => row.Id != id)) rows.Add(new(id, id));
                if (input.ItemsSource is not List<TrackOption> existing || !existing.SequenceEqual(rows)) input.ItemsSource = rows;
                input.SelectedItem = input.Items.Cast<TrackOption>().FirstOrDefault(row => row.Id == id);
                input.IsEnabled = context.IsEnabled && !busy;
            }
            finally { updating = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        input.SelectionChanged += async (_, _) =>
        {
            if (updating || disposed || busy || !context.IsEnabled || input.SelectedItem is not TrackOption selected) return;
            busy = true;
            try { await context.CommitAsync(new(JsonSerializer.SerializeToElement(selected.Id))); }
            finally { busy = false; Refresh(); }
        };
        input.DropDownOpened += (_, _) => Refresh();
        context.PropertyChanged += Changed;
        if (content is not null) content.Changed += Refresh;
        Refresh();
        return new(input, () =>
        {
            disposed = true; context.PropertyChanged -= Changed;
            if (content is not null) content.Changed -= Refresh;
        });
    }

    private sealed record TrackOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
