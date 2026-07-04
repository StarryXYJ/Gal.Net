namespace GalNet.Core.Widget;

/// <summary>
/// 控件大类定义（Button, Slider, Toggle, DialogueBox 等）。
/// </summary>
public sealed class WidgetCategory
{
    /// <summary>类别名称</summary>
    public string Name { get; }

    public WidgetCategory(string name) => Name = name;

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is WidgetCategory other && Name == other.Name;

    public override int GetHashCode() => Name.GetHashCode();

    // ── 内置类别 ──
    public static readonly WidgetCategory Button = new("Button");
    public static readonly WidgetCategory Slider = new("Slider");
    public static readonly WidgetCategory Toggle = new("Toggle");
    public static readonly WidgetCategory DialogueBox = new("DialogueBox");
    public static readonly WidgetCategory NvlBox = new("NvlBox");
    public static readonly WidgetCategory ChoicePanel = new("ChoicePanel");
    public static readonly WidgetCategory SaveSlot = new("SaveSlot");
    public static readonly WidgetCategory TitleButton = new("TitleButton");
}
