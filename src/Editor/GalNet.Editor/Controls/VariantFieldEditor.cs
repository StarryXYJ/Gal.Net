using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class VariantFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, SceneTargetCatalog scenes, bool selection, bool dimension = false)
    {
        var panel = new StackPanel { Spacing = 6, Name = selection ? "VariantInitialSelection" : "VariantDependentField" };
        var choices = new StackPanel { Spacing = 4 }; panel.Children.Add(choices);
        var manual = selection ? null : DefaultFieldEditors.CreateText(context);
        var repair = selection ? PropertyEditorSupport.Repair(context) : default;
        if (manual is not null) panel.Children.Add(manual.Control); else panel.Children.Add(repair.Button!);
        var lifetime = new CancellationTokenSource(); var disposed = false; var generation = 0;
        void Refresh()
        {
            if (disposed) return;
            var version = ++generation; choices.Children.Clear();
            var source = scenes.VariantsSource(context.HostContext as IEntryFieldOrigin);
            if (source?.Definition is not { } definition) return;
            var before = context.Value;
            async Task Commit(string key, string value)
            {
                if (disposed || version != generation || !context.IsEnabled || !scenes.IsCurrent(source.Snapshot)
                    || before.IsProvided != context.Value.IsProvided || before.IsProvided && !JsonElement.DeepEquals(before.Json!.Value, context.Value.Json!.Value)) return;
                var next = JsonSerializer.SerializeToElement(value);
                if (selection)
                {
                    JsonObject json;
                    try { json = context.EffectiveValue is { ValueKind: JsonValueKind.Object } current ? JsonNode.Parse(current.GetRawText())!.AsObject() : new(); }
                    catch (JsonException) { return; }
                    if (value.Length == 0) json.Remove(key); else json[key] = value;
                    next = JsonSerializer.SerializeToElement(json);
                }
                try { await context.CommitAsync(new(next), lifetime.Token); }
                catch (OperationCanceledException) { }
            }
            void Add(string key, string label, IReadOnlyList<string> options, string? current)
            {
                var combo = new ComboBox { Name = "VariantFieldChoice", ItemsSource = options, SelectedItem = options.Contains(current ?? "", StringComparer.Ordinal) ? current : null, PlaceholderText = label };
                if (selection) combo.ItemTemplate = new FuncDataTemplate<string>((item, _) => new TextBlock { Text = string.IsNullOrEmpty(item) ? scenes.Text["Variants.Inherit"] : item });
                combo.IsEnabled = context.IsEnabled;
                choices.Children.Add(new TextBlock { Text = label }); choices.Children.Add(combo);
                combo.SelectionChanged += async (_, _) => { if (combo.SelectedItem is string value) await Commit(key, value); };
            }
            if (selection)
            {
                // Unknown keys and invalid values stay available through the JSON repair editor.
                if (context.EffectiveValue is not { ValueKind: JsonValueKind.Object } values) return;
                foreach (var item in definition.Dimensions)
                {
                    var value = values.TryGetProperty(item.Key, out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString() : "";
                    Add(item.Key, item.Label ?? item.Key, new[] { "" }.Concat(item.Options).ToArray(), value);
                }
            }
            else
            {
                var options = dimension ? definition.Dimensions.Select(item => item.Key).ToArray() : definition.Dimensions.FirstOrDefault(item => item.Key == source.Dimension)?.Options ?? [];
                Add("", scenes.Text[dimension ? "Entry.Parameter.dimension" : "Entry.Parameter.value"], options,
                    context.EffectiveValue is { ValueKind: JsonValueKind.String } current ? current.GetString() : null);
            }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        async Task Prepare()
        {
            if (context.HostContext is not IEntryFieldOrigin { GroupId: { } group } origin) return;
            try { await scenes.PrepareAsync(group, origin.EntryId, token: lifetime.Token); if (!disposed) Refresh(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed) context.ReportIssue(new("variants.load", error.Message)); }
        }
        context.PropertyChanged += Changed; scenes.Text.PropertyChanged += Changed; scenes.Changed += Refresh; Refresh(); _ = Prepare();
        return new(panel, () => { disposed = true; generation++; lifetime.Cancel(); context.PropertyChanged -= Changed; scenes.Text.PropertyChanged -= Changed; scenes.Changed -= Refresh; manual?.Dispose(); repair.Lifetime?.Dispose(); lifetime.Dispose(); });
    }
}
