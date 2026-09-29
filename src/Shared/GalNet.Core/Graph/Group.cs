using EntryModel = GalNet.Core.Entry.Entry;

namespace GalNet.Core.Graph;

/// <summary>
/// 组节点 —— 代表一段线性内容序列，包含按序执行的条目列表。
/// </summary>
public sealed class Group : Node
{
    public override NodeType NodeType => NodeType.Group;

    /// <summary>该组包含的复杂条目列表（开发期编写）</summary>
    public List<EntryModel> Entries { get; init; } = [];
}
