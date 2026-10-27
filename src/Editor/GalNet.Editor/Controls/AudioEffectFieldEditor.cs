using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class AudioEffectFieldEditor
{
    private sealed record Option(string Id, string Label, SEffectDefinition? Definition = null, AudioFieldChoice? Choice = null)
    { public override string ToString() => Label; }

    public static FieldEditorLease Type(FieldEditorContext context, SceneTargetCatalog? catalog)
        => Create(context, catalog, type: true);
    public static FieldEditorLease Instance(FieldEditorContext context, SceneTargetCatalog? catalog)
        => Create(context, catalog, type: false);

    private static FieldEditorLease Create(FieldEditorContext context, SceneTargetCatalog? catalog, bool type)
    {
        var input = new ComboBox { Name = type ? "AudioEffectTypeInput" : "AudioEffectInstanceInput", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(input);
        var hint = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(hint);
        var lifetime = new CancellationTokenSource(); var disposed = false; var updating = false; var busy = false;
        var effects = catalog?.AudioCatalog ?? SEffectCatalog.Builtins;
        void Refresh()
        {
            if (disposed) return;
            updating = true;
            try
            {
                var origin = context.HostContext as IEntryParameterPatchOrigin;
                var scope = origin?.Parameters.GetValueOrDefault("scope") is { ValueKind: JsonValueKind.String } raw && raw.GetString() == "bus" ? SEffectScope.Bus : SEffectScope.Track;
                var rows = type ? effects.Definitions.Where(definition => definition.SupportedScopes.Contains(scope))
                    .Select(definition => new Option(definition.Id, catalog?.AudioTypeName(definition.Id) ?? PropertyEditorSupport.Text("Audio.Type." + definition.Id), definition)).ToList()
                    : (origin?.GroupId is { } group && catalog is not null ? catalog.FieldOptions(group, origin.EntryId, origin.EntryType, "instanceId") ?? [] : context.Options)
                    .Where(option => option.Label is AudioFieldChoice).Select(option => new Option(option.Id, option.Label.ToString()!, Choice: (AudioFieldChoice)option.Label)).ToList();
                Option? selected;
                if (type) selected = rows.FirstOrDefault(row => row.Id == (context.EffectiveValue is { ValueKind: JsonValueKind.String } typeValue ? typeValue.GetString() : null));
                else
                {
                    var all = origin?.Parameters.GetValueOrDefault("all") is { ValueKind: JsonValueKind.True };
                    selected = rows.FirstOrDefault(row => all ? row.Choice?.All == true : row.Choice is { All: false, Target: { } target }
                        && context.Value.Json is { ValueKind: JsonValueKind.String } value && target.Key.InstanceId == value.GetString());
                }
                if (selected is null && context.Value.Json is { ValueKind: JsonValueKind.String } unresolved && !string.IsNullOrEmpty(unresolved.GetString()))
                { selected = new("unresolved", PropertyEditorSupport.Text("Scene.Unresolved")); rows.Add(selected); }
                input.ItemsSource = rows; input.SelectedItem = selected;
                input.IsEnabled = context.IsEnabled && origin is not null && !busy;
                hint.Text = !type && rows.Select(row => row.Choice).OfType<AudioFieldChoice>().Any(choice => !choice.Snapshot.Complete)
                    ? PropertyEditorSupport.Text("Audio.AnalysisIncomplete") : null;
                hint.IsVisible = hint.Text is not null;
            }
            finally { updating = false; }
        }
        input.SelectionChanged += async (_, _) =>
        {
            if (disposed || updating || busy || !context.IsEnabled || input.SelectedItem is not Option option || context.HostContext is not IEntryParameterPatchOrigin origin) return;
            if (!type && (option.Choice is null || catalog is null || !catalog.IsCurrent(option.Choice.Snapshot)))
            { context.ReportIssue(new("entry.obsolete", PropertyEditorSupport.Text("Scene.Stale"))); Refresh(); return; }
            var patch = new Dictionary<string, FieldValue>();
            if (type)
            {
                if (option.Definition is null) return;
                patch["id"] = new(JsonSerializer.SerializeToElement(option.Definition.Id));
                patch["parameters"] = new(option.Definition.DefaultParameters);
            }
            else
            {
                patch["instanceId"] = option.Choice!.All ? default : new(JsonSerializer.SerializeToElement(option.Choice.Target!.Key.InstanceId));
                if (origin.EntryType == BuiltinEntryIds.StopAudioSEffect) patch["all"] = new(JsonSerializer.SerializeToElement(option.Choice.All));
                // A new update target starts a fresh patch; never transplant parameters from another algorithm.
                if (origin.EntryType == BuiltinEntryIds.UpdateAudioSEffect) patch["parameters"] = new(JsonSerializer.SerializeToElement(new { }));
            }
            busy = true;
            try
            {
                input.IsEnabled = false;
                if (await origin.CommitPatchAsync(patch, lifetime.Token) is { } issue && !disposed) context.ReportIssue(issue);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed) context.ReportIssue(new("field.commit", error.Message)); }
            finally { busy = false; Refresh(); }
        };
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        context.PropertyChanged += Changed;
        if (catalog is not null) catalog.Changed += Refresh;
        var locale = PropertyEditorSupport.Localize(Refresh);
        input.DropDownOpened += (_, _) => Refresh(); Refresh();
        return new(panel, () =>
        {
            disposed = true; lifetime.Cancel(); lifetime.Dispose(); locale.Dispose(); context.PropertyChanged -= Changed;
            if (catalog is not null) catalog.Changed -= Refresh;
        });
    }
}
