using DynamicLocalization.Core;
using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.View;

namespace GalNet.Core.Handler;

/// <summary>
/// 条目执行上下文 —— 传递给 EntryHandler 的上下文对象。
/// 通过 Runtime 访问所有游戏状态。
/// </summary>
public sealed class EntryContext
{
    /// <summary>当前执行的条目数据</summary>
    public required SimpleEntry Entry { get; init; }

    /// <summary>游戏运行时状态（位置、变量、场景等）</summary>
    public required IGameRuntime Runtime { get; init; }

    /// <summary>参数原始字典</summary>
    public Dictionary<string, string> Params => Entry.Params;

    /// <summary>IGameView 引用（快捷方式）</summary>
    public IGameView View => Runtime.View!;

    /// <summary>I18n 文本解析器（快捷方式）</summary>
    public ICultureService? I18n => Runtime.I18n;

    // ── 强类型参数读取 ──

    public string GetString(string key, string def = "") =>
        Params.TryGetValue(key, out var v) ? v : def;

    public bool GetBool(string key, bool def = false) =>
        Params.TryGetValue(key, out var v) && bool.TryParse(v, out var r) ? r : def;

    public float GetFloat(string key, float def = 0f) =>
        Params.TryGetValue(key, out var v) && float.TryParse(v, out var r) ? r : def;

    public int GetInt(string key, int def = 0) =>
        Params.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : def;

    /// <summary>通过 I18n 解析文本字段（speaker、content 等）。无 I18n 时原样返回。</summary>
    public string GetText(string key, string def = "") =>
        I18n?[GetString(key, def)] ?? GetString(key, def);
}
