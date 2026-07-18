using DynamicLocalization.Core;
using GalNet.Core.Entry;
using GalNet.Core.Runtime;
using GalNet.Core.View;

namespace GalNet.Core.Handler;

/// <summary>
/// Entry execution context passed to handlers.
/// </summary>
public sealed class EntryContext
{
    public required Entry.Entry Entry { get; init; }
    public required IGameRuntime Runtime { get; init; }

    public Dictionary<string, string> Params => Entry.Values;
    public IGameView View => Runtime.View!;
    public ICultureService? I18n => Runtime.I18n;

    public string GetString(string key, string def = "") =>
        Params.TryGetValue(key, out var v) ? v : def;

    public bool GetBool(string key, bool def = false) =>
        Params.TryGetValue(key, out var v) && bool.TryParse(v, out var r) ? r : def;

    public float GetFloat(string key, float def = 0f) =>
        Params.TryGetValue(key, out var v) && float.TryParse(v, out var r) ? r : def;

    public int GetInt(string key, int def = 0) =>
        Params.TryGetValue(key, out var v) && int.TryParse(v, out var r) ? r : def;

    public string GetText(string key, string def = "") =>
        I18n?[GetString(key, def)] ?? GetString(key, def);
}
