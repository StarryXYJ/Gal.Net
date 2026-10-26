using System.Collections.ObjectModel;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Runtime;

/// <summary>A validated, isolated candidate prepared before replacing a running scope.</summary>
public sealed class GameRestorePoint
{
    private GameRestorePoint(GameSnapshot snapshot, GameState state)
    {
        NodeId = snapshot.NodeId;
        EntryIndex = snapshot.EntryIndex;
        Variables = new ReadOnlyDictionary<string, GalVariable>(snapshot.Variables);
        State = state;
    }

    public string NodeId { get; }
    public int EntryIndex { get; }
    public IReadOnlyDictionary<string, GalVariable> Variables { get; }
    public GameState State { get; }

    public static GameRestorePoint Prepare(StateCatalog catalog, GameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != GameSnapshot.CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported save version '{snapshot.Version}'.");
        if (snapshot.Variables is null || snapshot.States is null || snapshot.EntryIndex < 0)
            throw new InvalidDataException("Invalid save position or state collections.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, variable) in snapshot.Variables)
            if (string.IsNullOrWhiteSpace(name) || variable is null || variable.Value is null ||
                string.IsNullOrWhiteSpace(variable.Uid) || !identities.Add(variable.Uid))
                throw new InvalidDataException("Save variables require unique identities and nonempty names.");
        var copy = SnapshotCopy.Clone(snapshot);
        return new(copy, GameState.Restore(catalog, copy.States));
    }
}
