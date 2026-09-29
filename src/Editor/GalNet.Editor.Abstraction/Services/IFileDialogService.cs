using System.Threading.Tasks;

namespace GalNet.Editor.Abstraction.Services;

/// <summary>
/// 文件对话框服务接口 —— 抽象平台文件/文件夹选择对话框，
/// 使 ViewModel 不直接依赖 Avalonia 的 Window/StorageProvider。
/// </summary>
public interface IFileDialogService
{
    /// <summary>打开文件夹选择对话框，返回选中文件夹路径（未选择返回 null）。</summary>
    Task<string?> OpenFolderPickerAsync(string title);
    Task<IReadOnlyList<string>> OpenFilePickerAsync(string title, bool allowMultiple = true);
}
