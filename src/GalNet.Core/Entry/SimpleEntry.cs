namespace GalNet.Core.Entry;

/// <summary>
/// 简单条目 —— 编译后的最小执行单元。Runtime 只处理 SimpleEntry。
/// </summary>
public sealed class SimpleEntry
{
    /// <summary>编译后的 ID。单条 = "Id"，多条 = "Id_1", "Id_2"...</summary>
    public string Id { get; init; } = "";

    /// <summary>来源复杂条目的 Id（调试 / 热更新定位用）</summary>
    public int SourceId { get; init; }

    /// <summary>条目类型标识，对应 EntryHandler.EntryType</summary>
    public string Type { get; init; } = "";

    /// <summary>通用条件表达式。false 则跳过。空 = 始终执行</summary>
    public string Condition { get; init; } = "";

    /// <summary>条目参数（键值对）</summary>
    public Dictionary<string, string> Params { get; init; } = new();
}
