namespace GalNet.Core.Assets;

/// <summary>Reserved identities for the one graph and its stable node-owned group resources.</summary>
public static class StoryResourceIds
{
    public const string Graph = "galnet:graph:main";
    public static string Group(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        return "galnet:group:" + nodeId;
    }
}
