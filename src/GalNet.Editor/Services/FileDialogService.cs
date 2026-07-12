using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public sealed class FileDialogService : IFileDialogService
{
    private static TopLevel? GetOwner() => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
        ? desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow
        : null;

    public async Task<string?> OpenFolderPickerAsync(string title)
    {
        var owner = GetOwner();
        if (owner?.StorageProvider is null) return null;
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<IReadOnlyList<string>> OpenFilePickerAsync(string title, bool allowMultiple = true)
    {
        var owner = GetOwner();
        if (owner?.StorageProvider is null) return [];
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = allowMultiple });
        return files.Select(x => x.Path.LocalPath).ToList();
    }
}
