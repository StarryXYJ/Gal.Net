using GalNet.Core.Settings;

namespace GalNet.Editor.Services;

/// <summary>
/// 编辑器设置管理 —— 读取/保存 EditorSettings。
/// </summary>
public interface IEditorSettingsService
{
    EditorSettings GetSettings();
    void SaveSettings();
}
