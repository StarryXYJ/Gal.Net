using GalNet.Core.Entry;
using GalNet.Core.View;

namespace GalNet.Core.Handler;

/// <summary>
/// 条目执行上下文 —— 传递给 EntryHandler 的上下文对象。
/// </summary>
public sealed class EntryContext
{
    /// <summary>当前执行的条目数据</summary>
    public required SimpleEntry Entry { get; init; }

    /// <summary>IGameView 引用</summary>
    public required IGameView View { get; init; }

    /// <summary>参数原始字典</summary>
    public Dictionary<string, string> Params => Entry.Params;

    // ── 强类型参数读取 ──

    public string GetString(string key, string def = "") =>
        Params.TryGetValue(key, out var v) ? v : def;

    public bool GetBool(string key, bool def = false) =>
        Params.TryGetValue(key, out var v) && bool.TryParse(v, out var r) ? r : def;

    public float GetFloat(string key, float def = 0f) =>
        Params.TryGetValue(key, out var v) && float.TryParse(v, out var r) ? r : def;

    public int GetInt(string key, int def = 0) =>
        Params.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : def;
}
