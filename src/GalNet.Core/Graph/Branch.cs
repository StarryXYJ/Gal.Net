namespace GalNet.Core.Graph;

/// <summary>
/// 分支节点 —— 选项分支或条件分支。
/// </summary>
public sealed class Branch : Node
{
    public override NodeType NodeType => NodeType.Branch;

    /// <summary>分支子类型：选项 / 条件</summary>
    public BranchType BranchType { get; set; }

    /// <summary>选项列表（仅 Choice 分支使用）。按顺序对应出边 0, 1, 2, ...</summary>
    public List<BranchOption> Options { get; init; } = [];

    /// <summary>条件列表（仅 Condition 分支使用）。按顺序匹配，命中则走对应出边</summary>
    public List<BranchCondition> Conditions { get; init; } = [];
}

/// <summary>
/// 选项分支的单个选项定义。
/// </summary>
public sealed class BranchOption
{
    /// <summary>展示文本（I18nKey）</summary>
    public string Text { get; init; } = "";

    /// <summary>选项是否可见的条件表达式。空 = 始终可见</summary>
    public string Condition { get; init; } = "";
}

/// <summary>
/// 条件分支的单个条件定义。按顺序匹配，首个 true 的生效。
/// </summary>
public sealed class BranchCondition
{
    /// <summary>条件表达式，如 "flag_route_a == true"</summary>
    public string Expression { get; init; } = "";
}
