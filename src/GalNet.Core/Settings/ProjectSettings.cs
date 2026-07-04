using GalNet.Core.I18n;

namespace GalNet.Core.Settings;

/// <summary>
/// 项目设置 —— 每个项目一份，持久化在项目目录 settings.json。
/// </summary>
public sealed class ProjectSettings : SettingsSection
{
    public override string SectionKey => "project";

    /// <summary>目标语言（编辑器默认编辑的语言）</summary>
    public I18nLocale TargetLocale { get; set; } = I18nLocale.ZhCn;

    /// <summary>项目支持的语言列表</summary>
    public List<I18nLocale> AvailableLocales { get; set; } = [I18nLocale.ZhCn];

    /// <summary>默认存档槽位数</summary>
    public int SaveSlotCount { get; set; } = 60;

    /// <summary>音效通道数（sfx1~sfxN 中的 N）</summary>
    public int SfxChannelCount { get; set; } = 4;

    /// <summary>默认分辨率宽度</summary>
    public int DefaultWidth { get; set; } = 1920;

    /// <summary>默认分辨率高度</summary>
    public int DefaultHeight { get; set; } = 1080;
}
