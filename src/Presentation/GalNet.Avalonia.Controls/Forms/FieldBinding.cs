using System.Text.Json;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Absent and explicit JSON null are separate values. Snapshots own their JSON.</summary>
public readonly record struct FieldValue
{
    public FieldValue(JsonElement? value) => Json = value?.Clone();
    public JsonElement? Json { get; }
    public bool IsProvided => Json.HasValue;
}

public sealed class FieldEditorOption(string id, object label, JsonElement value)
{
    public string Id { get; } = id;
    public object Label { get; } = label;
    public JsonElement Value { get; } = value.Clone();
}

/// <summary>Borrowed host binding. Changed may come from any thread; all other calls use the UI thread.</summary>
public interface IFieldBinding
{
    FieldDraftState? Draft => null;
    FieldValue Read();
    event EventHandler? Changed;
    Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken);
    bool IsEnabled => true;
    bool IsVisible => true;
    bool CanReset => true;
    object? Label => null;
    /// <summary>Optional host metadata, forwarded by scoped decorators without exposing their source binding.</summary>
    object? HostContext => null;
    IReadOnlyList<FieldEditorOption>? Options => null;
}

/// <summary>Optional synchronous preview path for controls that change continuously, such as sliders.</summary>
public interface IFieldPreviewBinding
{
    bool SupportsPreview { get; }
    ValueIssue? Preview(FieldValue value);
}
