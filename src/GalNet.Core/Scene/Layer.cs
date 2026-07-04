namespace GalNet.Core.Scene;

/// <summary>
/// Layer —— 背景和立绘的统一抽象。z 值越大越靠前。
/// </summary>
public sealed class Layer
{
    /// <summary>Layer 唯一 ID。同 ID 再次调用则替换</summary>
    public string Id { get; init; } = "";

    /// <summary>资源 ID 引用</summary>
    public string AssetId { get; set; } = "";

    public float X { get; set; }
    public float Y { get; set; }

    /// <summary>z-index 层叠顺序，越大越靠前。背景建议 0，立绘建议 5~20</summary>
    public float Z { get; set; }

    public bool Visible { get; set; } = true;
}
