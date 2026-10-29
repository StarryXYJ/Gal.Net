using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Runtime;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Presentation.Abstractions.Execution;

namespace GalNet.GameHost;

public sealed record DialogueHistoryCursor(int Count);

/// <summary>Run-owned completed dialogue visits. Only a small cursor participates in live snapshots.</summary>
public sealed class DialogueHistoryJournal
{
    public const string StateId = "host.dialogue.history";
    private readonly object _gate = new();
    private readonly List<DialogueHistoryLine> _lines = [];
    public event Action? Changed;

    public static IPrimitiveModule CreateModule() => new PrimitiveModule(registry =>
        registry.State(HistoryJsonContext.Default.DialogueHistoryCursorArray, StateId, _ => "current",
            validate: cursor => { if (cursor.Count < 0) throw new InvalidDataException("Invalid dialogue history cursor."); }));

    public IReadOnlyList<DialogueHistoryLine> Lines { get { lock (_gate) return _lines.ToArray(); } }

    public void Append(GameState state, DialogueHistoryLine line)
    {
        lock (_gate)
        {
            _lines.Add(line);
            state.Table<DialogueHistoryCursor>().Set(new(_lines.Count));
        }
        Changed?.Invoke();
    }

    public GameSnapshot Attach(GameSnapshot snapshot)
    {
        var result = SnapshotCopy.Clone(snapshot);
        lock (_gate)
        {
            var count = ReadCount(snapshot);
            if (count > _lines.Count) throw new InvalidDataException("Dialogue history is behind the saved checkpoint.");
            result.Attachments[StateId] = JsonSerializer.SerializeToElement(_lines.Take(count).ToArray(), HistoryJsonContext.Default.DialogueHistoryLineArray);
        }
        return result;
    }

    public void Restore(GameSnapshot? snapshot)
    {
        var lines = Decode(snapshot);
        lock (_gate) { _lines.Clear(); _lines.AddRange(lines); }
        Changed?.Invoke();
    }

    public static void Validate(GameSnapshot snapshot) => _ = Decode(snapshot);

    /// <summary>Older host saves had no history module. They start an empty journal; other state contracts remain strict.</summary>
    public static GameSnapshot PrepareSnapshot(GameSnapshot snapshot)
    {
        if (snapshot.States.ContainsKey(StateId)) return snapshot;
        if (snapshot.Attachments.ContainsKey(StateId)) throw new InvalidDataException("Dialogue history has no checkpoint cursor.");
        var result = SnapshotCopy.Clone(snapshot);
        result.States.Add(StateId, new(1, JsonSerializer.SerializeToElement(Array.Empty<DialogueHistoryCursor>(), HistoryJsonContext.Default.DialogueHistoryCursorArray)));
        return result;
    }

    public void Rewind(GameState state)
    {
        lock (_gate)
        {
            var cursors = state.Table<DialogueHistoryCursor>().Values;
            var count = Math.Min(cursors.Count == 0 ? 0 : cursors[0].Count, _lines.Count);
            if (count < _lines.Count) _lines.RemoveRange(count, _lines.Count - count);
            state.Table<DialogueHistoryCursor>().Set(new(count));
        }
        Changed?.Invoke();
    }

    private static DialogueHistoryLine[] Decode(GameSnapshot? snapshot)
    {
        if (snapshot is null) return [];
        var count = ReadCount(snapshot);
        var lines = snapshot.Attachments.TryGetValue(StateId, out var value)
            ? JsonSerializer.Deserialize(value, HistoryJsonContext.Default.DialogueHistoryLineArray)
                ?? throw new InvalidDataException("Missing dialogue history data.")
            : [];
        if (lines.Length != count || lines.Any(line => line is null || string.IsNullOrWhiteSpace(line.GroupId) ||
            string.IsNullOrWhiteSpace(line.EntryId) || line.Speaker is null || line.Text is null ||
            line.Voice is { } voice && (string.IsNullOrWhiteSpace(voice.AssetId) || string.IsNullOrWhiteSpace(voice.TrackId) || !float.IsFinite(voice.Gain) || voice.Gain < 0)))
            throw new InvalidDataException("Dialogue history does not match its saved checkpoint.");
        return lines;
    }

    private static int ReadCount(GameSnapshot snapshot)
    {
        if (!snapshot.States.TryGetValue(StateId, out var section)) return 0;
        if (section.Version != 1) throw new InvalidDataException("Unsupported dialogue history cursor.");
        var cursors = JsonSerializer.Deserialize(section.Payload, HistoryJsonContext.Default.DialogueHistoryCursorArray)
            ?? throw new InvalidDataException("Invalid dialogue history cursor.");
        if (cursors.Length > 1 || cursors.Any(cursor => cursor is null || cursor.Count < 0))
            throw new InvalidDataException("Invalid dialogue history cursor.");
        return cursors.FirstOrDefault()?.Count ?? 0;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DialogueHistoryCursor[]))]
[JsonSerializable(typeof(DialogueHistoryLine[]))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;
