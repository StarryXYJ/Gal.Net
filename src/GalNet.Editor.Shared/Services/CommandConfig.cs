namespace GalNet.Editor.Shared.Services;

/// <summary>
/// 可序列化的快捷键配置 —— CommandId → 快捷键文本（如 "Ctrl+Shift+S"）的映射。
/// </summary>
public class CommandConfig
{
    /// <summary>命令 Id → 快捷键文本的覆盖映射</summary>
    public Dictionary<string, string> GestureOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
