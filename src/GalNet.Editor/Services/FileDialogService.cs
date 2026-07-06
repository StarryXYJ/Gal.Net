using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GalNet.Editor.Services;

/// <summary>
/// 基于 Avalonia StorageProvider 的文件夹选择对话框实现。
/// 通过 TopLevel.GetTopLevel 获取顶层窗口，不依赖 Window 实例。
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    public async Task<string?> OpenFolderPickerAsync(string title)
    {
        // 从当前焦点窗口获取 StorageProvider
        var topLevel = TopLevel.GetTopLevel(TopLevel.GetTopLevel(null) as Window);
        if (topLevel?.StorageProvider == null)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }
}
