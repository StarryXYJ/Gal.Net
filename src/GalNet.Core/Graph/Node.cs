namespace GalNet.Core.Graph;

/// <summary>
/// 图节点的抽象基类。只有 Group 和 Branch 两种。
/// </summary>
public abstract class Node
{
    /// <summary>唯一标识（组 ID 或分支 ID）</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名称（编辑器中用）</summary>
    public string Name { get; set; } = "";

    /// <summary>节点类型</summary>
    public abstract NodeType NodeType { get; }
}
