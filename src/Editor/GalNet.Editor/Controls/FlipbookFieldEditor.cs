using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Numeric flipbook fields patch the author object without rewriting extension data.</summary>
internal static class FlipbookFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context)
    {
        var resolver = new ValueStructureResolver();
        using var structure = new ValueDocument(context.Field, null, resolver);
        var specs = structure.Root.Structure.Members;
        if (specs.Count == 0) { resolver.Dispose(); throw new NotSupportedException("Flipbook metadata is unavailable."); }
        var panel = new StackPanel { Name = "FlipbookEditor", Spacing = 6 };
        var children = new List<(FieldEditorContext Context, PropertyFieldBinding Binding, FieldEditorLease Lease, TextBlock Label)>();
        var disposed = false; var submitting = false; var baseline = context.Value;
        foreach (var spec in specs)
        {
            var field = spec.Field;
            var binding = new PropertyFieldBinding(context, field, spec.WireName, () => !disposed, async (candidate, token) =>
            {
                submitting = true;
                try
                {
                    if (!Valid(context.Field, candidate.Json))
                        return new("flipbook.invalid", PropertyEditorSupport.Text("Form.FlipbookInvalid"));
                    return await context.CommitAsync(candidate, token) ? null
                        : context.Issue ?? new("flipbook.invalid", PropertyEditorSupport.Text("Form.FlipbookInvalid"));
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or FormatException)
                { return new("flipbook.invalid", PropertyEditorSupport.Text("Form.FlipbookInvalid")); }
                finally { submitting = false; }
            }, resolver);
            var child = new FieldEditorContext(field, binding, context.Registry);
            var lease = (context.Registry?.Resolve(field) ?? NumericFieldEditors.CreateUpDown)(child); lease.Control.Name = "Flipbook" + spec.Field.LabelKey!.Split('.')[^1];
            var label = new TextBlock { Width = 90, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(label); Grid.SetColumn(lease.Control, 1); row.Children.Add(lease.Control); panel.Children.Add(row);
            children.Add((child, binding, lease, label));
        }
        var repair = PropertyEditorSupport.Repair(context); panel.Children.Add(repair.Button);
        var issue = new TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }; panel.Children.Add(issue);
        void Issues(object? sender, PropertyChangedEventArgs args)
        {
            var invalid = false;
            try { invalid = !Valid(context.Field, context.EffectiveValue); }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { invalid = true; }
            repair.Button.IsVisible = invalid;
            issue.Text = children.Select(child => child.Context.IssueMessage).FirstOrDefault(message => message is not null)
                ?? (invalid ? PropertyEditorSupport.Text("Form.FlipbookInvalid") : null);
            issue.IsVisible = issue.Text is not null;
        }
        foreach (var child in children) child.Context.PropertyChanged += Issues;
        var locale = PropertyEditorSupport.Localize(() =>
        {
            for (var index = 0; index < children.Count; index++) children[index].Label.Text = PropertyEditorSupport.Text(specs[index].Field.LabelKey!);
            Issues(null, new(null));
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
            panel.IsEnabled = context.IsEnabled; Issues(null, new(null));
        }
        context.PropertyChanged += Refresh; Refresh(null, new(null));
        return new(panel, () =>
        {
            disposed = true; context.PropertyChanged -= Refresh; repair.Lifetime.Dispose(); locale.Dispose();
            foreach (var child in children) { child.Context.PropertyChanged -= Issues; child.Lease.Dispose(); child.Context.Dispose(); }
            resolver.Dispose();
        });
    }
    private static bool Valid(FieldDescriptor field, JsonElement? value)
    {
        using var document = new ValueDocument(field, value);
        if (document.GetIterator().Any(property => property.Issue is { Code: not "field.unknown" })) return false;
        double Number(string name, double fallback) => document.FindProperty([name])?.Value is { } json ? json.GetDouble() : fallback;
        var columns = Number("columns", 1); var rows = Number("rows", 1); var count = Number("frameCount", 1);
        return columns * rows <= int.MaxValue && count <= columns * rows;
    }
}
