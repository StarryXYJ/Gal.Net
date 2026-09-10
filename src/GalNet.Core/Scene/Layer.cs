using System.Text.Json.Serialization;

namespace GalNet.Core.Scene;

/// <summary>
/// Resource-backed scene instance. Its Id is a stable opaque handle; z values sort front-to-back.
/// </summary>
public sealed class Layer : ISceneInstance
{
    /// <summary>Stable scene-instance handle. Editors generate this as an opaque GUID.</summary>
    public string Id { get; init; } = "";

    /// <summary>资源 ID 引用</summary>
    public string AssetId { get; set; } = "";

    public LayerTransform Transform { get; set; } = new();

    /// <summary>z-index 层叠顺序，越大越靠前。背景建议 0，立绘建议 5~20</summary>
    public float Z { get; set; }

    public LayerDisplayMode DisplayMode { get; set; } = LayerDisplayMode.Native;

    public bool Visible { get; set; } = true;

    // Read-only compatibility bridge for saves written before Transform existed.
    [JsonPropertyName("X")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? LegacyX { get => null; set { if (value is { } x) Transform.X = x; } }

    [JsonPropertyName("Y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? LegacyY { get => null; set { if (value is { } y) Transform.Y = y; } }
}
