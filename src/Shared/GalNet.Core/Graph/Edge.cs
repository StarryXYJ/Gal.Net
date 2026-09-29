namespace GalNet.Core.Graph;

/// <summary>
/// 节点间转移关系。从出口指向下一节点入口。
/// </summary>
public sealed class Edge
{
    /// <summary>唯一标识</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>来源节点 ID</summary>
    public string FromNodeId { get; init; } = "";

    /// <summary>来源出口索引。组只有一个出口（0），分支有多个出口（按分支选项/条件顺序）</summary>
    public int FromOutlet { get; init; }

    /// <summary>目标节点 ID</summary>
    public string ToNodeId { get; init; } = "";

    public Edge() { }

    public Edge(string fromNodeId, int fromOutlet, string toNodeId)
    {
        FromNodeId = fromNodeId;
        FromOutlet = fromOutlet;
        ToNodeId = toNodeId;
    }
}
