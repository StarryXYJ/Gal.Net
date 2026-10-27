using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>Numeric flipbook fields patch the author object without rewriting extension data.</summary>
internal static class FlipbookFieldEditor
{
    private static readonly (string Name, Type Type, int Default)[] Specs =
        [("Columns", typeof(int), 1), ("Rows", typeof(int), 1), ("FrameCount", typeof(int), 1), ("Index", typeof(float), 0)];

    public static FieldEditorLease Create(FieldEditorContext context)
    {
        var panel = new StackPanel { Name = "FlipbookEditor", Spacing = 6 };
        var children = new List<(FieldEditorContext Context, MemberBinding Binding, FieldEditorLease Lease, TextBlock Label)>();
        var disposed = false; var submitting = false; var baseline = context.Value;
        foreach (var spec in Specs)
        {
            var field = new FieldDescriptor(spec.Name, new(spec.Type, constraints: JsonSerializer.SerializeToElement(new { minimum = spec.Default == 0 ? 0 : 1 })),
                defaultValue: JsonSerializer.SerializeToElement(spec.Default), labelKey: "Form.Flipbook." + spec.Name);
            var binding = new MemberBinding(context, field, () => disposed, async value =>
            {
                submitting = true;
                try
                {
                    var root = Read(context);
                    var key = Wire(root, spec.Name);
                    if (value.Json is { } json) root[key] = JsonNode.Parse(json.GetRawText()); else root.Remove(key);
                    if (!Valid(root))
                        return new("flipbook.invalid", PropertyEditorSupport.Text("Form.FlipbookInvalid"));
                    return await context.CommitAsync(new(JsonSerializer.SerializeToElement(root))) ? null
                        : context.Issue ?? new("flipbook.invalid", PropertyEditorSupport.Text("Form.FlipbookInvalid"));
                }
                catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or FormatException)
                { return new("flipbook.invalid", PropertyEditorSupport.Text("Form.FlipbookInvalid")); }
                finally { submitting = false; }
            });
            var child = new FieldEditorContext(field, binding, context.Registry);
            var lease = (context.Registry?.Resolve(field) ?? NumericFieldEditors.CreateUpDown)(child); lease.Control.Name = "Flipbook" + spec.Name;
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
            try { invalid = !Valid(Read(context)); }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { invalid = true; }
            repair.Button.IsVisible = invalid;
            issue.Text = children.Select(child => child.Context.IssueMessage).FirstOrDefault(message => message is not null)
                ?? (invalid ? PropertyEditorSupport.Text("Form.FlipbookInvalid") : null);
            issue.IsVisible = issue.Text is not null;
        }
        foreach (var child in children) child.Context.PropertyChanged += Issues;
        var locale = PropertyEditorSupport.Localize(() =>
        {
            for (var index = 0; index < children.Count; index++) children[index].Label.Text = PropertyEditorSupport.Text("Form.Flipbook." + Specs[index].Name);
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
        });
    }
    private static JsonObject Read(FieldEditorContext context) => context.EffectiveValue is null or { ValueKind: JsonValueKind.Null }
        ? new() : JsonNode.Parse(context.EffectiveValue.Value.GetRawText()) as JsonObject ?? throw new InvalidDataException();
    private static string Wire(JsonObject root, string name)
    {
        var matches = root.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException();
        return matches.SingleOrDefault() ?? char.ToLowerInvariant(name[0]) + name[1..];
    }
    private static double Number(JsonObject root, string name, double fallback) => root[Wire(root, name)] is { } node ? node.GetValue<double>() : fallback;
    private static bool Valid(JsonObject root)
    {
        foreach (var spec in Specs)
            if (root.TryGetPropertyValue(Wire(root, spec.Name), out var node) &&
                ValueValidation.Validate(new(spec.Type, constraints: JsonSerializer.SerializeToElement(new { minimum = spec.Default == 0 ? 0 : 1 })), JsonSerializer.SerializeToElement(node)) is not null)
                return false;
        var columns = Number(root, "Columns", 1); var rows = Number(root, "Rows", 1); var count = Number(root, "FrameCount", 1);
        return columns * rows <= int.MaxValue && count <= columns * rows;
    }
    private sealed class MemberBinding(FieldEditorContext parent, FieldDescriptor member, Func<bool> disposed, Func<FieldValue, Task<ValueIssue?>> commit) : IFieldBinding
    {
        public FieldDraftState? Draft => parent.DraftState?.Child?.Invoke(member, member.Id);
        public bool IsEnabled => !disposed() && parent.IsEnabled;
        public FieldValue Read()
        {
            try { var root = FlipbookFieldEditor.Read(parent); return root.TryGetPropertyValue(Wire(root, member.Id), out var node) ? new(JsonSerializer.SerializeToElement(node)) : default; }
            catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException) { return new(JsonSerializer.SerializeToElement("?")); }
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
