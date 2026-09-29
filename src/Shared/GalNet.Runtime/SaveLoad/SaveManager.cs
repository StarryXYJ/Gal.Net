using System.Text.Json;
using GalNet.Core.Runtime;

namespace GalNet.Runtime.SaveLoad;

/// <summary>
/// 存档管理器 —— 序列化/反序列化 GameSnapshot。
/// </summary>
public static class SaveManager
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>序列化存档到 JSON 字符串。</summary>
    public static string Serialize(GameSnapshot data)
    {
        return JsonSerializer.Serialize(data, Options);
    }

    /// <summary>从 JSON 字符串反序列化。</summary>
    public static GameSnapshot? Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var version) ||
            !version.TryGetInt32(out var formatVersion) ||
            formatVersion != GameSnapshot.CurrentFormatVersion)
            return null;

        var snapshot = JsonSerializer.Deserialize<GameSnapshot>(document.RootElement.GetRawText(), Options);
        return snapshot?.Version == GameSnapshot.CurrentFormatVersion ? snapshot : null;
    }

    /// <summary>保存到文件。</summary>
    public static void SaveToFile(GameSnapshot data, string path)
    {
        var json = Serialize(data);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }

    /// <summary>从文件加载。</summary>
    public static GameSnapshot? LoadFromFile(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return Deserialize(json);
    }
}
