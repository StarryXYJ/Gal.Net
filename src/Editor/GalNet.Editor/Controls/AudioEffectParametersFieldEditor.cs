using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Parameters;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class AudioEffectParametersFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, SceneTargetCatalog? catalog)
    {
        var panel = new StackPanel { Name = "AudioEffectParameters", Spacing = 6 };
        var rows = new StackPanel { Spacing = 6 }; panel.Children.Add(rows);
        var notice = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(notice);
        var repair = PropertyEditorSupport.Repair(context); panel.Children.Add(repair.Button);
        var children = new List<(FieldEditorContext Context, MemberBinding Binding, FieldEditorLease Lease, TextBlock Label)>();
        var disposed = false; var submitting = false; var refreshing = false; string? currentKey = null; var baseline = context.Value;
        var effects = catalog?.AudioCatalog ?? SEffectCatalog.Builtins;
        string? baseNotice = null;
        void Issues(object? sender, PropertyChangedEventArgs args)
        {
            notice.Text = children.Select(child => child.Context.Issue?.Message).FirstOrDefault(message => message is not null) ?? baseNotice;
            notice.IsVisible = notice.Text is not null;
        }
        void Clear() { foreach (var child in children) { child.Context.PropertyChanged -= Issues; child.Lease.Dispose(); child.Context.Dispose(); } children.Clear(); rows.Children.Clear(); }
        void Refresh()
        {
            if (disposed || refreshing) return;
            refreshing = true;
            try
            {
                var origin = context.HostContext as IEntryParameterPatchOrigin;
                var update = origin?.EntryType == BuiltinEntryIds.UpdateAudioSEffect;
                var type = origin?.Parameters.GetValueOrDefault("id") is { ValueKind: JsonValueKind.String } raw ? raw.GetString() : null;
                if (update)
                {
                    type = null;
                    if (catalog is not null && origin?.GroupId is { } group && origin.Parameters.GetValueOrDefault("instanceId") is { ValueKind: JsonValueKind.String } instance)
                    {
                        var target = catalog.QueryAudio(group, origin.EntryId).Targets.FirstOrDefault(target => target.Key.InstanceId == instance.GetString());
                        if (target?.Types.Count == 1) type = target.Types[0];
                    }
                }
                string Part(string name) => origin?.Parameters.GetValueOrDefault(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";
                var key = JsonSerializer.Serialize(new[] { type, Part("scope"), Part("targetId"), Part("instanceId") });
                if (key != currentKey)
                {
                    foreach (var child in children) child.Context.RevertDraft();
                    Clear(); currentKey = key;
                    if (type is not null && effects.TryGet(type, out var definition))
                        foreach (var parameter in definition.Parameters)
                        {
                            var field = update ? new FieldDescriptor(parameter.Name, parameter.Field.Value) : parameter.Field;
                            var binding = new MemberBinding(context, field, () => disposed || !context.IsEnabled, async value =>
                            {
                                submitting = true;
                                try
                                {
                                    var root = Read(context);
                                    if (value.Json is { } json) root[field.Id] = JsonNode.Parse(json.GetRawText()); else root.Remove(field.Id);
                                    return await context.CommitAsync(new(JsonSerializer.SerializeToElement(root))) ? null : context.Issue ?? new("audio.parameters", "Cannot update audio parameters.");
                                }
                                finally { submitting = false; }
                            });
                            var child = new FieldEditorContext(field, binding, context.Registry);
                            var lease = DefaultFieldEditors.CreateNumeric(child); lease.Control.Name = "AudioParameter_" + field.Id;
                            var label = new TextBlock { Width = 105, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
                            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
                            row.Children.Add(label); Grid.SetColumn(lease.Control, 1); row.Children.Add(lease.Control); rows.Children.Add(row);
                            children.Add((child, binding, lease, label));
                            child.PropertyChanged += Issues;
                        }
                }
                var current = context.Value;
                var changed = baseline.IsProvided != current.IsProvided || baseline.IsProvided && !JsonElement.DeepEquals(baseline.Json!.Value, current.Json!.Value);
                baseline = current;
                foreach (var child in children)
                {
                    child.Label.Text = PropertyEditorSupport.Text("Audio.Parameter." + child.Context.Field.Id);
                    if (changed && !submitting) child.Context.RevertDraft();
                    child.Binding.Refresh();
                }
                var invalid = type is null || !effects.TryGet(type, out var selected);
                try
                {
                    var parameters = Read(context);
                    invalid |= type is not null && effects.TryGet(type, out selected) && parameters.Any(pair => !selected.Parameters.Any(parameter => parameter.Name == pair.Key));
                }
                catch (InvalidDataException) { invalid = true; }
                repair.Button.IsVisible = invalid;
                baseNotice = invalid ? PropertyEditorSupport.Text("Audio.SelectEffect") : update ? PropertyEditorSupport.Text("Audio.UnchangedParameters") : null;
                Issues(null, new(null));
                panel.IsEnabled = context.IsEnabled;
            }
            finally { refreshing = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        context.PropertyChanged += Changed;
        if (catalog is not null) catalog.Changed += Refresh;
        var locale = PropertyEditorSupport.Localize(Refresh); Refresh();
        return new(panel, () =>
        {
            disposed = true; context.PropertyChanged -= Changed;
            if (catalog is not null) catalog.Changed -= Refresh;
            Clear(); repair.Lifetime.Dispose(); locale.Dispose();
        });
    }
    private static JsonObject Read(FieldEditorContext context)
    {
        if (context.EffectiveValue is not { } value) return new();
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("Effect parameters must be an object with unique fields.");
        return JsonNode.Parse(value.GetRawText())!.AsObject();
    }
    private sealed class MemberBinding(FieldEditorContext parent, FieldDescriptor field, Func<bool> unavailable, Func<FieldValue, Task<ValueIssue?>> commit) : IFieldBinding
    {
        public FieldDraftState? Draft => parent.DraftState?.Child?.Invoke(@field, @field.Id);
        public bool IsEnabled => !unavailable();
        public FieldValue Read()
        {
            try { return AudioEffectParametersFieldEditor.Read(parent).TryGetPropertyValue(field.Id, out var value) ? new(JsonSerializer.SerializeToElement(value)) : default; }
            catch (InvalidDataException) { return new(JsonSerializer.SerializeToElement("?")); }
        }
        public event EventHandler? Changed;
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return IsEnabled ? commit(value) : Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry is unavailable.")); }
    }
}
