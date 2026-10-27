using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Projects a preset's shader metadata onto its parameters object without exposing a raw JSON editor.</summary>
internal static class PresetShaderParametersFieldEditor
{
    private static readonly HashSet<string> Generated = new(StringComparer.Ordinal) { "time", "speed", "flicker" };

    public static FieldEditorLease Create(FieldEditorContext context, ProjectSceneTargets? programs)
    {
        var panel = new StackPanel { Name = "PresetShaderParameters", Spacing = 6 };
        var rows = new StackPanel { Spacing = 6 }; panel.Children.Add(rows);
        var notice = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(notice);
        var children = new List<(FieldEditorContext Context, MemberBinding Binding, FieldEditorLease Lease)>();
        var disposed = false; var refreshing = false; var submitting = false;
        var selectedProgram = "";
        ShaderEffectDescriptor? selectedDescriptor = null;
        var baseline = context.Value;

        void Clear()
        {
            foreach (var child in children) { child.Lease.Dispose(); child.Context.Dispose(); }
            children.Clear(); rows.Children.Clear();
        }
        void Refresh()
        {
            if (disposed || refreshing) return;
            refreshing = true;
            try
            {
                var program = CurrentProgram(context);
                var descriptor = programs?.ProgramDescriptor(program);
                if (program != selectedProgram || !ReferenceEquals(descriptor, selectedDescriptor))
                {
                    Clear(); selectedProgram = program; selectedDescriptor = descriptor;
                    if (descriptor is not null)
                        foreach (var parameter in descriptor.Parameters.Where(parameter => !Generated.Contains(parameter.Name)))
                        {
                            var field = ShaderParameterFormBinding.CreateField(parameter);
                            var boundProgram = program;
                            var binding = new MemberBinding(context, field,
                                () => disposed || !context.IsEnabled || CurrentProgram(context) != boundProgram || !TryRead(context, out _),
                                async value =>
                                {
                                    if (!TryRead(context, out var values)) return new("shader.parameters", "Shader parameters must be an object.");
                                    if (value.Json is { } json) values[field.Id] = json.Clone(); else values.Remove(field.Id);
                                    submitting = true;
                                    try
                                    {
                                        return await context.CommitAsync(new(JsonSerializer.SerializeToElement(values))) ? null
                                            : context.Issue ?? new("shader.parameters", "The shader parameters could not be saved.");
                                    }
                                    finally { submitting = false; }
                                });
                            var child = new FieldEditorContext(field, binding, context.Registry);
                            var lease = context.Registry!.Resolve(field)(child);
                            var row = new Grid { ColumnDefinitions = new("120,*"), ColumnSpacing = 8 };
                            row.Children.Add(new TextBlock
                            {
                                Text = string.IsNullOrWhiteSpace(parameter.Group) ? parameter.DisplayName ?? parameter.Name
                                    : parameter.Group + " / " + (parameter.DisplayName ?? parameter.Name),
                                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
                            });
                            Grid.SetColumn(lease.Control, 1); row.Children.Add(lease.Control); rows.Children.Add(row);
                            children.Add((child, binding, lease));
                        }
                }
                var current = context.Value;
                var changed = baseline.IsProvided != current.IsProvided || baseline.IsProvided && !JsonElement.DeepEquals(baseline.Json!.Value, current.Json!.Value);
                baseline = current;
                foreach (var child in children)
                {
                    if (changed && !submitting) child.Context.RevertDraft();
                    child.Binding.Refresh();
                }
                notice.Text = descriptor is null ? PropertyEditorSupport.Text("Entry.ShaderMetadataUnavailable")
                    : !TryRead(context, out _) ? "Shader parameters must be an object." : null;
                notice.IsVisible = notice.Text is not null;
                panel.IsEnabled = context.IsEnabled;
            }
            finally { refreshing = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        context.PropertyChanged += Changed;
        if (programs is not null) programs.Changed += Refresh;
        Refresh();
        return new(panel, () =>
        {
            disposed = true; context.PropertyChanged -= Changed;
            if (programs is not null) programs.Changed -= Refresh;
            Clear();
        });
    }

    private static string CurrentProgram(FieldEditorContext context)
    {
        var origin = context.HostContext as IEntryParameterPatchOrigin;
        return origin?.Parameters.GetValueOrDefault("program") is { ValueKind: JsonValueKind.String } id ? id.GetString() ?? "" : "";
    }

    private static bool TryRead(FieldEditorContext context, out Dictionary<string, JsonElement> values)
    {
        values = new(StringComparer.Ordinal);
        if (context.EffectiveValue is not { ValueKind: JsonValueKind.Object } root) return false;
        foreach (var property in root.EnumerateObject())
            if (!values.TryAdd(property.Name, property.Value.Clone())) return false;
        return true;
    }

    private sealed class MemberBinding(FieldEditorContext parent, FieldDescriptor descriptor, Func<bool> unavailable,
        Func<FieldValue, Task<ValueIssue?>> commit) : IFieldBinding
    {
        public FieldDraftState? Draft => parent.DraftState?.Child?.Invoke(descriptor, descriptor.Id);
        public bool IsEnabled => !unavailable();
        public FieldValue Read() => TryRead(parent, out var values) && values.TryGetValue(descriptor.Id, out var value) ? new(value) : default;
        public event EventHandler? Changed;
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return IsEnabled ? commit(value) : Task.FromResult<ValueIssue?>(new("entry.obsolete", "Shader selection changed.")); }
    }
}
