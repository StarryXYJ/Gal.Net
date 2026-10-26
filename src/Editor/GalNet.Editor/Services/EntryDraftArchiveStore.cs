using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed record ArchivedEntryDraft(string Id, SceneAuthorAddress Owner, string Type, string RootId, string[] Path,
    string FieldType, string Baseline, string Text, bool JsonText, ValueIssue? Issue, bool Conflicted);
public sealed record EntryDraftArchiveLimits(int Count = 1000, int Bytes = 16 * 1024 * 1024);

/// <summary>Durable input recovery, independent of author files and the bounded undo stack.</summary>
internal sealed class EntryDraftArchiveStore(string root, EntryDraftArchiveLimits limits)
{
    private const string RelativePath = ".galnet/editor/input-archive.json";
    private sealed record ArchiveFile(int Version, IReadOnlyList<ArchivedEntryDraft> Drafts);
    public IReadOnlyList<ArchivedEntryDraft> Read()
    {
        var path = new ProjectRawReader(root).Resolve(RelativePath);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > limits.Bytes) throw new InvalidDataException("Entry input recovery exceeds its budget. Preserve the recovery file before repairing it.");
        var file = JsonSerializer.Deserialize<ArchiveFile>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Entry input recovery is empty.");
        if (file.Version != 1 || file.Drafts is null || file.Drafts.Count > limits.Count || file.Drafts.Any(draft => draft is null || string.IsNullOrWhiteSpace(draft.Id)
            || draft.Owner is null || draft.Owner.GroupId is null || draft.Owner.EntryId is null || draft.Type is null || draft.RootId is null || draft.Path is null
            || draft.Path.Any(segment => segment is null) || draft.FieldType is null || draft.Baseline is null || draft.Text is null)
            || file.Drafts.Select(draft => draft.Id).Distinct(StringComparer.Ordinal).Count() != file.Drafts.Count)
            throw new InvalidDataException("Entry input recovery is corrupt or has an unsupported version.");
        return file.Drafts;
    }
    public static byte[] Encode(IReadOnlyList<ArchivedEntryDraft> drafts, EntryDraftArchiveLimits limits)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ArchiveFile(1, drafts));
        if (drafts.Count > limits.Count || bytes.Length > limits.Bytes)
            throw new GalNet.Editor.Abstraction.Services.EditorOperationException("entry.archiveFull", "Input recovery is full. Copy or discard archived inputs in Problems before removing more entries.");
        return bytes;
    }
    public void Write(byte[] bytes)
    {
        var path = new ProjectRawReader(root).Resolve(RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
