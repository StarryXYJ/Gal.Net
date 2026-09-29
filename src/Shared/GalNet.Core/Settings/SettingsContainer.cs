namespace GalNet.Core.Settings;

/// <summary>
/// 统一的设置容器 —— 按 SectionKey 存取，持久化到 JSON。
/// </summary>
public sealed class SettingsContainer
{
    private readonly Dictionary<string, SettingsSection> _sections = new();

    /// <summary>获取指定类型的设置节（若不存在则创建默认实例）</summary>
    public T Get<T>() where T : SettingsSection, new()
    {
        var key = new T().SectionKey;
        if (_sections.TryGetValue(key, out var section) && section is T typed)
            return typed;

        var def = new T();
        _sections[key] = def;
        return def;
    }

    /// <summary>设置指定类型的设置节</summary>
    public void Set<T>(T section) where T : SettingsSection
    {
        _sections[section.SectionKey] = section;
    }

    /// <summary>获取所有设置节</summary>
    public IReadOnlyList<SettingsSection> GetAll() => _sections.Values.ToList().AsReadOnly();
}
