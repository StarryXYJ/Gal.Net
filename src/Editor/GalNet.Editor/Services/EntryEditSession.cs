using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

/// <summary>Project-owned drafts. Closing a view never releases unsubmitted author input.</summary>
public sealed class EntryEditSession : IDisposable
{
    private readonly Dictionary<string, Draft> _drafts = new(StringComparer.Ordinal);
    private readonly IDisposable _registration;
    private readonly IProjectContentSession _content;
    private readonly GameTextAuthoringService _texts;
    private readonly IEditorLocalizationService _localization;
    public EntryEditSession(IProjectContentSession content, GameTextAuthoringService texts, IEditorLocalizationService localization)
    {
        _content = content; _texts = texts; _localization = localization;
        _registration = content.RegisterDraft(() => PendingCount > 0, CommitAsync);
        content.Changed += CheckSources;
    }
    public event Action? Changed;
    public int PendingCount => _drafts.Values.Count(draft => draft.State.IsDirty || !draft.State.CanCommit);
    public IEnumerable<Draft> Pending => _drafts.Values.Where(draft => draft.State.IsDirty || !draft.State.CanCommit);

    public FieldDraftState Get(EntryEditorItemViewModel owner, FieldDescriptor field) => Get(owner, field, field, []);
    private FieldDraftState Get(EntryEditorItemViewModel owner, FieldDescriptor root, FieldDescriptor field, string[] path)
    {
        var key = owner.AuthorGroupId + "/" + owner.StableId + "/" + root.Id + "/" + string.Join("/", path);
        // The author object is a generation boundary: undo may restore the same ID with a new source.
        key += "/" + owner.Type + "/" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(owner.Source);
        if (_drafts.TryGetValue(key, out var found)) return found.State;
        var type = owner.Type;
        string Snapshot()
        {
            if (!owner.IsCurrent || owner.Source.Type != type) return "<obsolete>";
            var value = owner.Source.Parameters.GetValueOrDefault(root.Id);
            return value.GetRawTextSafe() + (root.Semantic == FieldValueSemantic.LocalizedText && value.ValueKind == JsonValueKind.String ? "\n" + _texts.Read(value.GetString()!) : "");
        }
        var state = new FieldDraftState(Snapshot);
        var draft = new Draft(owner, root, field, path, type, state);
        _drafts.Add(key, draft);
        state.Child = (child, segment) => Get(owner, root, child, [.. path, segment]);
        state.Commit = () => CommitAsync(draft);
        state.Changed += OnChanged;
        return state;
    }
    private void OnChanged() { _content.Refresh(); Changed?.Invoke(); }
    private void CheckSources()
    {
        foreach (var draft in _drafts.Values) draft.State.CheckSource();
    }
    private ValueIssue Error(string code) => new(code, _localization["Uiux.Draft." + code]);
    private async Task<ValueIssue?> CommitAsync(Draft draft)
    {
        var state = draft.State;
        if (!state.CanCommit) return Error("Composing");
        if (!state.IsDirty) return null;
        if (state.IsConflicted || !draft.Owner.IsCurrent || draft.Owner.Type != draft.Type) return Error("Conflict");
        try
        {
            FieldValue candidate;
            GameTextDraft? textDraft = null;
            try
            {
                if (draft.Field.Semantic == FieldValueSemantic.LocalizedText)
                {
                    var key = draft.Owner.Source.Parameters.GetValueOrDefault(draft.Root.Id);
                    textDraft = _texts.Register(_texts.Capture(key.ValueKind == JsonValueKind.String ? key.GetString()! : "", draft.Owner.StableId), state.Text);
                    candidate = new(JsonSerializer.SerializeToElement(textDraft.Token));
                }
                else candidate = !state.JsonText && state.Text.Length == 0 && draft.Field.Value.ValueType != typeof(string)
                    && (!draft.Field.IsRequired || draft.Field.DefaultValue is not null) ? default
                    : new(state.JsonText ? JsonSerializer.Deserialize<JsonElement>(state.Text) : ValueCodec.ParseEditorText(draft.Field.Value, state.Text));
                if (candidate.Json is { } value && ValueValidation.Validate(draft.Field.Value, value) is { } invalid) return invalid;
                if (!candidate.IsProvided && draft.Field.IsRequired && draft.Field.DefaultValue is null) return Error("Invalid");
                if (draft.Path.Length > 0)
                {
                    var source = draft.Owner.Source.Parameters.GetValueOrDefault(draft.Root.Id);
                    var encoded = source.ValueKind == JsonValueKind.String;
                    var root = source.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? new JsonObject()
                        : JsonNode.Parse(encoded ? source.GetString()! : source.GetRawText())!;
                    JsonNode node = root;
                    foreach (var segment in draft.Path[..^1])
                    {
                        var child = Child(node, segment);
                        if (child is null && node is JsonObject obj) { child = new JsonObject(); obj[Wire(obj, segment)] = child; }
                        node = child ?? throw new InvalidDataException("The draft's object or list target is no longer available.");
                    }
                    var last = draft.Path[^1];
                    var replacement = candidate.Json is { } json ? JsonNode.Parse(json.GetRawText()) : null;
                    if (node is JsonArray array) array[int.Parse(last, System.Globalization.CultureInfo.InvariantCulture)] = replacement;
                    else if (candidate.IsProvided) node[Wire(node, last)] = replacement;
                    else ((JsonObject)node).Remove(Wire(node, last));
                    candidate = new(encoded ? JsonSerializer.SerializeToElement(root.ToJsonString()) : JsonSerializer.SerializeToElement(root));
                }
                var result = await draft.Owner.CommitParameterAsync(draft.Root, candidate, CancellationToken.None);
                if (result is null) state.Clear();
                return result;
            }
            finally { if (textDraft is not null) _texts.Release(textDraft); }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or InvalidDataException)
        { return new("field.draft", exception.Message); }
    }
    private static string Wire(JsonNode node, string name)
    {
        if (node is not JsonObject obj) return name;
        var matches = obj.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length > 1 ? throw new InvalidDataException("The draft's property name is ambiguous.") : matches.SingleOrDefault() ?? name;
    }
    private static JsonNode? Child(JsonNode node, string name) => node is JsonArray array ? array[int.Parse(name, System.Globalization.CultureInfo.InvariantCulture)] : node[Wire(node, name)];
    public async Task CommitAsync()
    {
        var failures = new List<string>();
        foreach (var draft in Pending.ToArray())
            if (await CommitAsync(draft) is { } issue)
            {
                draft.State.Update(draft.State.Text, draft.State.IsDirty, issue, draft.State.JsonText);
                failures.Add($"{draft.Owner.AuthorGroupId} / {draft.Owner.Id} / {draft.Root.Id}: {issue.Message}");
            }
        if (failures.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, failures));
    }
    public void Dispose()
    {
        _registration.Dispose();
        _content.Changed -= CheckSources;
        foreach (var draft in _drafts.Values) draft.State.Changed -= OnChanged;
        _drafts.Clear(); Changed = null;
    }
    public sealed record Draft(EntryEditorItemViewModel Owner, FieldDescriptor Root, FieldDescriptor Field, string[] Path, string Type, FieldDraftState State);
    internal string Summary(EntryEditorItemViewModel owner) => string.Join(" · ", owner.Source.Parameters
        .Where(pair => pair.Key is "content" or "speaker" or "asset" or "assetId" or "target" or "expression" or "handleId")
        .Select(pair => pair.Key == "content" && pair.Value.ValueKind == JsonValueKind.String ? _texts.Read(pair.Value.GetString()!) ?? pair.Value.GetString()
            : pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() : pair.Value.GetRawText())
        .Where(value => !string.IsNullOrWhiteSpace(value))).Replace('\n', ' ');
}

internal static class DraftJson
{
    public static string GetRawTextSafe(this JsonElement json) => json.ValueKind == JsonValueKind.Undefined ? "<absent>" : json.GetRawText();
}
