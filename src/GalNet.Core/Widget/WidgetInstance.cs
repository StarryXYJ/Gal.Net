namespace GalNet.Core.Widget;

/// <summary>
/// 控件实例 —— 模板 + 填入参数值后的具体控件，有唯一 ID。
/// </summary>
public sealed class WidgetInstance
{
    /// <summary>实例唯一 ID（如 "new_game_btn", "default_dialogue"）</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>引用的模板 ID</summary>
    public string TemplateId { get; set; } = "";

    /// <summary>实例配置参数</summary>
    public WidgetConfig? Config { get; set; }
}
