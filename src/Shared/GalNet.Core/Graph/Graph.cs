namespace GalNet.Core.Graph;

/// <summary>
/// 主图 —— 一个游戏对应一张图，由节点和边组成。
/// </summary>
public sealed class Graph
{
    /// <summary>图唯一标识</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>图名称</summary>
    public string Name { get; set; } = "";

    /// <summary>入口节点 ID</summary>
    public string RootNodeId { get; set; } = "";

    /// <summary>所有节点（Group + Branch）</summary>
    public List<Node> Nodes { get; init; } = [];

    /// <summary>所有边</summary>
    public List<Edge> Edges { get; init; } = [];
}
