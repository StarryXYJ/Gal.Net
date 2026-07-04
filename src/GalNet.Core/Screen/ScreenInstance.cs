namespace GalNet.Core.Screen;

/// <summary>
/// 页面实例 —— 模板 + 填入参数值后的具体页面，有唯一 ID。
/// </summary>
public sealed class ScreenInstance
{
    /// <summary>实例唯一 ID（如 "title_page", "settings_page"）</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>引用的模板 ID</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>实例配置参数</summary>
    public ScreenConfig? Config { get; set; }
}
