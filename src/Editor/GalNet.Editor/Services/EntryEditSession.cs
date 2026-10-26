using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Services;

/// <summary>Project-owned drafts. Closing a view never releases unsubmitted author input.</summary>
public sealed partial class EntryEditSession : IDisposable
{
    private sealed record DraftKey(string GroupId, string EntryId, string RootId, string Path, Guid Incarnation);
    private readonly DraftRepository _drafts = new();
    private bool _disposed;
    private readonly IDisposable _registration;
    private readonly IDisposable _structureRegistration;
    private readonly IProjectContentSession _content;
    private readonly GameTextAuthoringService _texts;
    private readonly IEditorLocalizationService _localization;
    public EntryEditSession(IProjectContentSession content, GameTextAuthoringService texts, IEditorLocalizationService localization,
        IEditorDocumentService? documents = null, IProjectService? projects = null, EntryDraftArchiveLimits? limits = null, GalNet.Editor.History.EditorHistories? histories = null)
    {
        _content = content; _texts = texts; _localization = localization;
        _documents = documents; _projects = projects; _limits = limits ?? new();
        _registration = content.RegisterDraft(() => PendingCount > 0, context => CommitAsync(context), DiscardDrafts);
        _structureRegistration = content.RegisterStructureTransition((before, after, publish) =>
        {
            var surviving = after.Graph.Nodes.Where(node => node.Type == "Group").Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var removed = before.GroupEntries.Where(pair => !surviving.Contains(pair.Key)).SelectMany(pair => pair.Value.Select(entry =>
            { var address = new GalNet.Editor.Shared.Services.SceneAuthorAddress(pair.Key, entry.StableId); return new EntryAddressTransition(address, address); })).ToArray();
            if (removed.Length == 0) publish();
            else
                try { ApplyAuthorEdit(removed, publish, histories?.Content.AuthorChanges); }
                catch (GalNet.Editor.History.HistoryRepairRequiredException failure) { content.BlockWrites(failure); throw; }
        });
        content.Changed += CheckSources;
    }
    public event Action? Changed;
    public int PendingCount => _drafts.Values.Count(draft => draft.State.IsDirty || !draft.State.CanCommit);
    public IEnumerable<Draft> Pending => _drafts.Values.Where(draft => draft.State.IsDirty || !draft.State.CanCommit);

    public FieldDraftState Get(EntryEditorItemViewModel owner, FieldDescriptor field) => Get(owner, field, field, []);
    private FieldDraftState Get(EntryEditorItemViewModel owner, FieldDescriptor root, FieldDescriptor field, string[] path) => Get(new DraftOwner(owner), root, field, path);
    private FieldDraftState Get(DraftOwner ownership, FieldDescriptor root, FieldDescriptor field, string[] path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var owner = ownership.Get();
        var incarnation = owner.DraftIncarnation;
        var key = new DraftKey(owner.AuthorGroupId!, owner.StableId, root.Id, JsonSerializer.Serialize(path), incarnation);
        if (_drafts.TryGetValue(key, out var found)) return found.State;
        var type = owner.Type;
        string Snapshot()
        {
            var current = ownership.Get();
            if (!current.IsCurrent || current.Source.Type != type || current.DraftIncarnation != incarnation) return "<obsolete>";
            var value = current.Source.Parameters.GetValueOrDefault(root.Id);
            return value.GetRawTextSafe() + (root.Semantic == FieldValueSemantic.LocalizedText && value.ValueKind == JsonValueKind.String ? "\n" + _texts.Read(value.GetString()!) : "");
        }
        var state = new FieldDraftState(Snapshot);
        var draft = new Draft(ownership, root, field, path, type, state, incarnation);
        _drafts.Add(key, draft);
        state.Child = (child, segment) => Get(ownership, root, child, [.. path, segment]);
        state.Commit = () => CommitAsync(draft);
        draft.OnChanged = () => { _drafts.Track(draft); OnChanged(); };
        state.Changed += draft.OnChanged;
        TryRestore(draft);
        return state;
    }
    private void OnChanged() { Revision++; _content.Refresh(); NotifyChanged(); }
    private void NotifyChanged()
    {
        if (Changed is null) return;
        foreach (Action observer in Changed.GetInvocationList())
            try { observer(); } catch (Exception failure) { Serilog.Log.Error(failure, "An entry input observer failed"); }
    }
    private void CheckSources()
    {
        foreach (var draft in _drafts.Values) draft.State.CheckSource();
        NotifyChanged();
    }
    private void DiscardDrafts()
    {
        foreach (var draft in _drafts.Values) draft.State.Clear();
        Changed?.Invoke();
    }
    private ValueIssue Error(string code) => new(code, _localization["Uiux.Draft." + code]);
    private async Task<ValueIssue?> CommitAsync(Draft draft)
    {
        if (_disposed) return Error("Conflict");
        var state = draft.State;
        if (!state.CanCommit) return Error("Composing");
        if (!state.IsDirty) return null;
        if (state.IsConflicted || !draft.Owner.IsCurrent || draft.Owner.Type != draft.Type || draft.Owner.DraftIncarnation != draft.Incarnation) return Error("Conflict");
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
    public Task CommitAsync() => CommitAsync(default(ProjectDraftCommitContext));
    private async Task CommitAsync(ProjectDraftCommitContext context)
    {
        var failures = new List<string>();
        foreach (var draft in Pending.Where(draft => context.RemovingGroups?.Contains(draft.Owner.AuthorGroupId!) != true).ToArray())
            if (await CommitAsync(draft) is { } issue)
            {
                draft.State.Update(draft.State.Text, draft.State.IsDirty, issue, draft.State.JsonText);
                failures.Add($"{draft.Owner.AuthorGroupId} / {draft.Owner.Id} / {draft.Root.Id}: {issue.Message}");
            }
        if (failures.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, failures));
    }
    public void Dispose()
    {
        _disposed = true;
        _registration.Dispose();
        _structureRegistration.Dispose();
        _content.Changed -= CheckSources;
        foreach (var draft in _drafts.Values) draft.State.Changed -= draft.OnChanged;
        _drafts.Clear(); _archives.Clear(); _restoreRequested.Clear(); Changed = null;
    }
    public sealed class Draft
    {
        private readonly DraftOwner _owner;
        internal Draft(DraftOwner owner, FieldDescriptor root, FieldDescriptor field, string[] path, string type, FieldDraftState state, Guid incarnation)
        { _owner = owner; Root = root; Field = field; Path = path; Type = type; State = state; Incarnation = incarnation; }
        public EntryEditorItemViewModel Owner => _owner.Get();
        public FieldDescriptor Root { get; }
        public FieldDescriptor Field { get; }
        public string[] Path { get; }
        public string Type { get; }
        public FieldDraftState State { get; }
        public Guid Incarnation { get; }
        internal Action? OnChanged { get; set; }
    }
    internal string Summary(EntryEditorItemViewModel owner) => owner.FormatSummary(_texts.Read);
}

internal static class DraftJson
{
    public static string GetRawTextSafe(this JsonElement json) => json.ValueKind == JsonValueKind.Undefined ? "<absent>" : json.GetRawText();
}
