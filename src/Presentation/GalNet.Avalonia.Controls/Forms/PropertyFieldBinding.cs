using System.Text.Json;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>One child property, backed by the parent's current JSON and author transaction.</summary>
public sealed class PropertyFieldBinding(FieldEditorContext parent, FieldDescriptor memberField, string segment,
    Func<bool> available, Func<FieldValue, CancellationToken, Task<ValueIssue?>> submit, ValueStructureResolver? resolver = null) : IFieldBinding
{
    public FieldDraftState? Draft => parent.DraftState?.Child?.Invoke(memberField, segment);
    public object? HostContext => parent.HostContext;
    public IReadOnlyList<string>? PropertySegments => [.. parent.PropertySegments ?? [], segment];
    public bool CanReset => parent.AllowsChildReset;
    public bool IsEnabled => available() && parent.IsEnabled;
    public bool IsVisible => parent.IsVisible;
    public FieldValue Read()
    {
        using var document = new ValueDocument(parent.Field, parent.EffectiveValue, resolver);
        var property = document.FindProperty([segment]);
        return property is not null ? new(property.Value) : document.Root.Issue is not null
            ? new(JsonSerializer.SerializeToElement("?")) : default;
    }
    public event EventHandler? Changed;
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
    public async Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEnabled) return new("structure.obsolete", "The property binding is no longer available.");
        using var document = new ValueDocument(parent.Field, parent.Value.Json, resolver);
        var patch = document.Patch([segment], value.Json, cancellationToken);
        if (patch.Issue is not null) return patch.Issue;
        return await submit(new(patch.Value), cancellationToken);
    }
}
