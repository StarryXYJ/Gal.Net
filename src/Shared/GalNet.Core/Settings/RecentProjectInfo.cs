namespace GalNet.Core.Settings;

/// <summary>
/// 最近项目信息 —— 持久化到编辑器设置中。
/// </summary>
public sealed class RecentProjectInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime LastOpened { get; set; }
}
