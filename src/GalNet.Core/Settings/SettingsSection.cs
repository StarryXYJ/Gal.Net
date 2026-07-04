namespace GalNet.Core.Settings;

/// <summary>
/// 设置节抽象基类 —— JSON 可序列化/反序列化的一节设置。
/// </summary>
public abstract class SettingsSection
{
    /// <summary>节的唯一 Key</summary>
    public abstract string SectionKey { get; }
}
