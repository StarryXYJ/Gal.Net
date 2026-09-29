namespace GalNet.Core.Settings;

/// <summary>
/// 存档相关设置 —— 继承项目设置中与存档相关的配置。
/// </summary>
public sealed class SaveSettings : SettingsSection
{
    public override string SectionKey => "save";

    /// <summary>最大存档槽位数</summary>
    public int MaxSlots { get; set; } = 60;

    /// <summary>缩略图宽度</summary>
    public int ThumbnailWidth { get; set; } = 320;

    /// <summary>缩略图高度</summary>
    public int ThumbnailHeight { get; set; } = 180;

    public static SaveSettings FromProject(ProjectSettings project) => new()
    {
        MaxSlots = project.SaveSlotCount
    };
}
