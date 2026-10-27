using GalNet.Core.Serialization;
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
        AllowDuplicateProperties = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>序列化存档到 JSON 字符串。</summary>
    public static string Serialize(GameSnapshot data)
    {
        return JsonCodec.Serialize(data, Options);
    }

    /// <summary>从 JSON 字符串反序列化。</summary>
    public static GameSnapshot? Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var version) ||
            !version.TryGetInt32(out var formatVersion) ||
            formatVersion != GameSnapshot.CurrentFormatVersion)
            return null;

        var snapshot = JsonCodec.Deserialize<GameSnapshot>(document.RootElement, Options);
        return snapshot?.Version == GameSnapshot.CurrentFormatVersion ? snapshot : null;
    }

}
