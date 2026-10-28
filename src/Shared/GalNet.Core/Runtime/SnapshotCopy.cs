using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Runtime;

/// <summary>External snapshots own their variables and immutable JSON payloads.</summary>
public static class SnapshotCopy
{
    public static GameSnapshot Clone(GameSnapshot snapshot) => Clone(snapshot, null, null);
    public static GameSnapshot Clone(GameSnapshot snapshot, string? contentBuildId, IEnumerable<string>? storyPackages) => new()
    {
        Version = snapshot.Version,
        NodeId = snapshot.NodeId,
        EntryIndex = snapshot.EntryIndex,
        ContentBuildId = contentBuildId ?? snapshot.ContentBuildId,
        StoryPackages = (storyPackages ?? snapshot.StoryPackages).ToArray(),
        Variables = snapshot.Variables.ToDictionary(pair => pair.Key, pair => CloneVariable(pair.Value), StringComparer.Ordinal),
        States = snapshot.States.ToDictionary(pair => pair.Key,
            pair => new StateSection(pair.Value.Version, pair.Value.Payload.Clone()), StringComparer.Ordinal)
    };
    public static GalVariable CloneVariable(GalVariable variable) => variable.Clone();
}
