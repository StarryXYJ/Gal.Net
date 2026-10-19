using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Layout;
using GalNet.Core.Parameters;
using GalNet.Core.Scene;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Edits only the selected transform member, retaining author casing and extension data.</summary>
internal static class TransformFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context)
    {
        var defaults = new LayerTransform();
        var specs = new[] { ("X", defaults.X), ("Y", defaults.Y), ("RotationDegrees", defaults.RotationDegrees), ("ScaleX", defaults.ScaleX), ("ScaleY", defaults.ScaleY) };
        var panel = new Grid { Name = "TransformEditor", ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        var values = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 6, ColumnSpacing = 8 };
        panel.Children.Add(values);
        var repair = PropertyEditorSupport.Repair(context); repair.Button.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(repair.Button, 1); panel.Children.Add(repair.Button);
        var issueLabel = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var children = new List<(FieldEditorContext Context, MemberBinding Binding, FieldEditorLease Lease, TextBlock Label)>();
        var disposed = false; var submitting = false; var baseline = context.Value;
        for (var index = 0; index < specs.Length; index++)
        {
            var (name, fallback) = specs[index];
            var field = new FieldDescriptor(name, new(typeof(float)), defaultValue: JsonSerializer.SerializeToElement(fallback));
            var binding = new MemberBinding(context, name, () => disposed, async value =>
            {
                submitting = true;
                try
                {
                    var (root, encoded) = Read(context);
                    var key = Wire(root, name);
                    if (value.Json is { } json) root[key] = JsonNode.Parse(json.GetRawText()); else root.Remove(key);
                    var candidate = encoded ? JsonSerializer.SerializeToElement(root.ToJsonString()) : JsonSerializer.SerializeToElement(root);
                    return await context.CommitAsync(new(candidate)) ? null : context.Issue ?? new("transform.invalid", PropertyEditorSupport.Text("Form.TransformInvalid"));
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
                { return new("transform.invalid", PropertyEditorSupport.Text("Form.TransformInvalid")); }
                finally { submitting = false; }
            });
            var child = new FieldEditorContext(field, binding, context.Registry);
            var lease = DefaultFieldEditors.CreateNumeric(child);
            lease.Control.Name = "Transform" + name;
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 72, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
            var cell = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 6 };
            cell.Children.Add(label); Grid.SetColumn(lease.Control, 1); cell.Children.Add(lease.Control);
            var row = index < 2 ? 0 : index == 2 ? 1 : 2;
            Grid.SetRow(cell, row); Grid.SetColumn(cell, index is 1 or 4 ? 1 : 0);
            if (index == 2) Grid.SetColumnSpan(cell, 2);
            values.Children.Add(cell); children.Add((child, binding, lease, label));
            child.PropertyChanged += ChildChanged;
        }
        panel.RowDefinitions = new("Auto,Auto"); Grid.SetRow(issueLabel, 1); panel.Children.Add(issueLabel);
        void ChildChanged(object? sender, PropertyChangedEventArgs args)
        {
            var invalid = false;
            try
            {
                var (root, _) = Read(context);
                invalid = specs.Any(spec => root.TryGetPropertyValue(Wire(root, spec.Item1), out var node) &&
                    ValueValidation.Validate(new(typeof(float)), JsonSerializer.SerializeToElement(node)) is not null);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { invalid = true; }
            repair.Button.IsVisible = invalid;
            panel.ColumnSpacing = invalid ? 6 : 0;
            issueLabel.Text = children.Select(child => child.Context.Issue?.Message).FirstOrDefault(message => message is not null)
                ?? (invalid ? PropertyEditorSupport.Text("Form.TransformInvalid") : null);
            issueLabel.IsVisible = issueLabel.Text is not null;
        }
        var locale = PropertyEditorSupport.Localize(() =>
        {
            for (var index = 0; index < children.Count; index++) children[index].Label.Text = PropertyEditorSupport.Text("Form.Transform." + specs[index].Item1);
            ChildChanged(null, new(null));
        });
        void Refresh(object? sender, PropertyChangedEventArgs args)
        {
            if (disposed) return;
            var current = context.Value;
            var changed = baseline.IsProvided != current.IsProvided || baseline.IsProvided && !JsonElement.DeepEquals(baseline.Json!.Value, current.Json!.Value);
            baseline = current;
            foreach (var child in children)
            {
                if (changed && !submitting) child.Context.RevertDraft();
                child.Binding.Refresh();
            }
            panel.IsEnabled = context.IsEnabled;
            ChildChanged(null, new(null));
        }
        context.PropertyChanged += Refresh; Refresh(null, new(null)); ChildChanged(null, new(null));
        return new(panel, () =>
        {
            disposed = true; context.PropertyChanged -= Refresh; repair.Lifetime.Dispose(); locale.Dispose();
            foreach (var child in children) { child.Context.PropertyChanged -= ChildChanged; child.Context.Dispose(); child.Lease.Dispose(); }
        });
    }
    private static (JsonObject Root, bool Encoded) Read(FieldEditorContext context)
    {
        var json = context.EffectiveValue;
        if (json is null || json.Value.ValueKind == JsonValueKind.Null) return (new(), false);
        var encoded = json.Value.ValueKind == JsonValueKind.String;
        return (JsonNode.Parse(encoded ? json.Value.GetString()! : json.Value.GetRawText()) as JsonObject
            ?? throw new InvalidDataException(), encoded);
    }
    private static string Wire(JsonObject root, string name)
    {
        var matches = root.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException();
        // Existing runtime Transform uses CLR property names, with case-insensitive reads.
        return matches.SingleOrDefault() ?? name;
    }
    private sealed class MemberBinding(FieldEditorContext parent, string name, Func<bool> disposed, Func<FieldValue, Task<ValueIssue?>> commit) : IFieldBinding
    {
        public bool IsEnabled => !disposed() && parent.IsEnabled;
        public FieldValue Read()
        {
            try
            {
                var (root, _) = TransformFieldEditor.Read(parent);
                return root.TryGetPropertyValue(Wire(root, name), out var node) ? new(JsonSerializer.SerializeToElement(node)) : default;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
            { return new(JsonSerializer.SerializeToElement(PropertyEditorSupport.Text("Form.TransformInvalid"))); }
        }
        public event EventHandler? Changed;
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return IsEnabled ? commit(value) : Task.FromResult<ValueIssue?>(new("entry.obsolete", "Entry schema has changed."));
        }
    }
}
