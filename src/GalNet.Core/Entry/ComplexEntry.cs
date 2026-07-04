namespace GalNet.Core.Entry;

/// <summary>
/// 复杂条目 —— 开发者编写的高层条目。编译时展开为若干 SimpleEntry。
/// </summary>
public abstract class ComplexEntry
{
    /// <summary>组内顺序 ID（行号）</summary>
    public int Id { get; init; }

    /// <summary>条目类型标识</summary>
    public string Type { get; init; } = "";

    /// <summary>通用条件表达式。false 则跳过。空 = 始终执行</summary>
    public string Condition { get; init; } = "";

    /// <summary>条目参数（键值对）</summary>
    public Dictionary<string, string> Params { get; init; } = new();

    /// <summary>编译为本组简单条目。子条目 ID 遵循 Id 方案（单条 = "Id"，多条 = "Id_1", "Id_2"...）</summary>
    public abstract IReadOnlyList<SimpleEntry> Compile();
}
