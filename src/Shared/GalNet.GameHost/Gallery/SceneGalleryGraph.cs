using GalNet.Core.Graph;

namespace GalNet.GameHost.Gallery;

public static class SceneGalleryGraph
{
    public static Graph Create(Graph source, string resourceId)
    {
        var group = source.Nodes.OfType<Group>().SingleOrDefault(node => node.ResourceId == resourceId)
            ?? throw new InvalidDataException($"Gallery Group '{resourceId}' does not exist.");
        if (!group.IsAvailable) throw new InvalidDataException("The Gallery scene's story package is not installed.");
        return new Graph { Id = source.Id, Name = source.Name, RootNodeId = group.Id, Nodes = [group],
            SaveVariables = source.SaveVariables, PlayerVariables = source.PlayerVariables,
            ContentBuildId = source.ContentBuildId, InstalledStoryPackages = source.InstalledStoryPackages };
    }
}
