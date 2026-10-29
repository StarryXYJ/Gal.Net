using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Edits only the selected transform member, retaining author casing and extension data.</summary>
internal static class TransformFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context)
    {
        var resolver = new ValueStructureResolver();
        using var structure = new ValueDocument(context.Field, null, resolver);
        var specs = structure.Root.Structure.Members;
        if (specs.Count == 0) { resolver.Dispose(); throw new NotSupportedException("Transform metadata is unavailable."); }
        var panel = new Grid { Name = "TransformEditor", ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        var values = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 6, ColumnSpacing = 8 };
        panel.Children.Add(values);
        var repair = PropertyEditorSupport.Repair(context); repair.Button.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(repair.Button, 1); panel.Children.Add(repair.Button);
        var issueLabel = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        var children = new List<(FieldEditorContext Context, PropertyFieldBinding Binding, FieldEditorLease Lease, TextBlock Label)>();
        var disposed = false; var submitting = false; var baseline = context.Value;
        for (var index = 0; index < specs.Count; index++)
        {
            var name = specs[index].WireName;
            var field = specs[index].Field;
            var binding = new PropertyFieldBinding(context, field, name, () => !disposed, async (candidate, token) =>
            {
                submitting = true;
                try
                {
                    return await context.CommitAsync(candidate, token) ? null : context.Issue ?? new("transform.invalid", PropertyEditorSupport.Text("Form.TransformInvalid"));
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException)
                { return new("transform.invalid", PropertyEditorSupport.Text("Form.TransformInvalid")); }
                finally { submitting = false; }
            }, resolver);
            var child = new FieldEditorContext(field, binding, context.Registry);
            var lease = (context.Registry?.Resolve(field) ?? NumericFieldEditors.CreateUpDown)(child);
            lease.Control.Name = "Transform" + field.LabelKey!.Split('.')[^1];
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
                using var snapshot = new ValueDocument(context.Field, context.EffectiveValue, resolver);
                invalid = snapshot.GetIterator().Any(property => property.Issue is { Code: not "field.unknown" });
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { invalid = true; }
            repair.Button.IsVisible = invalid;
            panel.ColumnSpacing = invalid ? 6 : 0;
            issueLabel.Text = children.Select(child => child.Context.IssueMessage).FirstOrDefault(message => message is not null)
                ?? (invalid ? PropertyEditorSupport.Text("Form.TransformInvalid") : null);
            issueLabel.IsVisible = issueLabel.Text is not null;
        }
        var locale = PropertyEditorSupport.Localize(() =>
        {
            for (var index = 0; index < children.Count; index++) children[index].Label.Text = PropertyEditorSupport.Text(specs[index].Field.LabelKey!);
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
            resolver.Dispose();
        });
    }
}
