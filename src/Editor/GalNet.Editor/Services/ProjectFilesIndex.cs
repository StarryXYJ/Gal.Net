using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Read-only enumeration; every mutation goes through ProjectFileAuthoringService.</summary>
public sealed class ProjectFilesIndex(IProjectService projects, IAssetCatalogService assets, IEditorLocalizationService localization)
{
    private static readonly string[] Hidden = [".galnet", ".git", ".vs", ".idea", ".editor-tx", ".editor-trash", "bin", "obj", "Output", "Temp"];
    public string Root { get; } = projects.Current?.RootPath ?? throw new InvalidOperationException("No project is open.");
    private static bool Visible(string full) => !Hidden.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
    private bool HasDirectories(string path) => Directory.EnumerateDirectories(new ProjectRawReader(Root).Resolve(path)).Any(Visible);
    public bool DirectoryExists(string path) { try { return Directory.Exists(new ProjectRawReader(Root).Resolve(path)); } catch (Exception) { return false; } }
    public IEnumerable<ProjectFileItem> Read(string directory)
    {
        var reader = new ProjectRawReader(Root);
        foreach (var full in Directory.EnumerateFileSystemEntries(reader.Resolve(directory)).OrderBy(path => !Directory.Exists(path)).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (!Visible(full)) continue;
            var path = reader.Relative(full);
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            var reason = "";
            try { ProjectFilePaths.Editable(path); } catch (Exception error) { reason = EditorText.Error(error).Render(localization); }
            var asset = path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? assets.GetEntry(path[7..]) : null;
            yield return new() { Path = path, FullPath = full, IsDirectory = Directory.Exists(full), WriteTimeUtc = File.GetLastWriteTimeUtc(full), Type = Directory.Exists(full) ? "folder" : asset?.Type ?? Path.GetExtension(path).TrimStart('.'), ProtectionReason = reason };
        }
    }
    public IEnumerable<ProjectDirectoryNode> Directories(string path) => Read(path).Where(item => item.IsDirectory).Select(item => new ProjectDirectoryNode(item.Path, item.Name, Directories, HasDirectories(item.Path)));
    // Type filtering stays separate from the search syntax for future scope/type controls.
    public IEnumerable<ProjectFileItem> Search(string name, string? type = null)
    {
        var pending = new Stack<string>(); pending.Push("");
        while (pending.TryPop(out var directory))
            foreach (var item in Read(directory))
            {
                if (item.IsDirectory) pending.Push(item.Path);
                if (item.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && (type is null || item.Type.Equals(type, StringComparison.OrdinalIgnoreCase))) yield return item;
                else item.Dispose();
            }
    }
}
