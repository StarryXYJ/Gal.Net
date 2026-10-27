using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Edits a fixed-size shader vector as one parameter value.</summary>
internal static class ShaderVectorFieldEditor
{
    private static readonly string[] Components = ["X", "Y", "Z", "W"];

    public static FieldEditorLease Create(FieldEditorContext context, int dimension)
    {
        if (dimension is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(dimension));
        var panel = new StackPanel { Name = "ShaderVectorEditor", Spacing = 6 };
        var fields = new Grid { ColumnDefinitions = new(string.Join(',', Enumerable.Repeat("*", dimension))), ColumnSpacing = 6 };
        panel.Children.Add(fields);
        var repair = PropertyEditorSupport.Repair(context);
        panel.Children.Add(repair.Button);
        var issue = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(issue);
        var children = new List<(FieldEditorContext Context, ComponentBinding Binding, FieldEditorLease Lease)>();
        var disposed = false;
        var submitting = false;
        var baseline = context.Value;
        for (var index = 0; index < dimension; index++)
        {
            var component = index;
            var name = Components[index];
            var field = new FieldDescriptor(name, new(typeof(float)));
            var binding = new ComponentBinding(context, field, () => disposed, dimension, component, async value =>
            {
                if (value.Json is not { } json || !json.TryGetSingle(out var number) || !float.IsFinite(number))
                    return new("shader.vector", "Enter a finite number.");
                if (!TryRead(context, dimension, out var values))
                    return new("shader.vector", "Repair the vector JSON value before editing its components.");
                values[component] = number;
                submitting = true;
                try
                {
                    return await context.CommitValueAsync(values) ? null : context.Issue ?? new("shader.vector", "The vector could not be saved.");
                }
                finally { submitting = false; }
            });
            var child = new FieldEditorContext(field, binding, context.Registry);
            var lease = DefaultFieldEditors.CreateNumeric(child);
            lease.Control.Name = "ShaderVector" + name;
            var cell = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 4 };
            cell.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(lease.Control, 1);
            cell.Children.Add(lease.Control);
            Grid.SetColumn(cell, index);
            fields.Children.Add(cell);
            children.Add((child, binding, lease));
        }

        void Issues(object? sender, PropertyChangedEventArgs args)
        {
            var valid = TryRead(context, dimension, out _);
            repair.Button.IsVisible = !valid;
            issue.Text = children.Select(child => child.Context.Issue?.Message).FirstOrDefault(message => message is not null)
                ?? (!valid ? "Repair the vector JSON value before editing its components." : context.Issue?.Message);
            issue.IsVisible = issue.Text is not null;
        }
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
            Issues(null, new(null));
            panel.IsEnabled = context.IsEnabled;
        }
        context.PropertyChanged += Refresh;
        foreach (var child in children) child.Context.PropertyChanged += Issues;
        Refresh(null, new(null));
        return new(panel, () =>
        {
            disposed = true;
            context.PropertyChanged -= Refresh;
            repair.Lifetime.Dispose();
            foreach (var child in children)
            {
                child.Context.PropertyChanged -= Issues;
                child.Lease.Dispose();
                child.Context.Dispose();
            }
        });
    }

    private static bool TryRead(FieldEditorContext context, int dimension, out float[] values)
    {
        values = [];
        if (context.EffectiveValue is not { ValueKind: JsonValueKind.Array } json || json.GetArrayLength() != dimension) return false;
        var items = new float[dimension];
        for (var index = 0; index < dimension; index++)
            if (json[index].ValueKind != JsonValueKind.Number || !json[index].TryGetSingle(out items[index]) || !float.IsFinite(items[index])) return false;
        values = items;
        return true;
    }

    private sealed class ComponentBinding(FieldEditorContext parent, FieldDescriptor descriptor, Func<bool> disposed,
        int dimension, int component, Func<FieldValue, Task<ValueIssue?>> commit) : IFieldBinding
    {
        public FieldDraftState? Draft => parent.DraftState?.Child?.Invoke(descriptor, descriptor.Id);
        public bool IsEnabled => !disposed() && parent.IsEnabled && TryRead(parent, dimension, out _);
        public FieldValue Read() => TryRead(parent, dimension, out var values)
            ? new(JsonSerializer.SerializeToElement(values[component])) : default;
        public event EventHandler? Changed;
        public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return IsEnabled ? commit(value) : Task.FromResult<ValueIssue?>(new("shader.vector", "The vector is unavailable."));
        }
    }
}
