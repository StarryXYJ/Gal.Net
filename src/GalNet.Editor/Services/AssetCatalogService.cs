using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models;
using GalNet.Editor.Services.Interfaces;

namespace GalNet.Editor.Services;

/// <summary>Owns editor-side Assets file operations and keeps sidecar JSON metadata in lockstep.</summary>
public sealed class AssetCatalogService : IAssetCatalogService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".ogg", ".flac", ".m4a" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mkv", ".avi", ".mov" };
    private readonly IProjectService _projects;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly Action<GalNet.Editor.Abstraction.Project.GalProject?> _projectChangedHandler;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watchDebounce;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event Action? Changed;
    public AssetCatalogService(IProjectService projects)
    {
        _projects = projects;
        _projectChangedHandler = _ => StartWatching();
        _projects.CurrentChanged += _projectChangedHandler;
        StartWatching();
    }

    public IReadOnlyList<AssetEntry> GetDirectory(string relativeDirectory)
    {
        var root = RootOrNull(); if (root is null) return [];
        var directory = Resolve(relativeDirectory); if (!Directory.Exists(directory)) return [];
        var entries = new List<AssetEntry>();
        foreach (var path in Directory.EnumerateDirectories(directory)) entries.Add(CreateDirectory(path));
        foreach (var path in Directory.EnumerateFiles(directory).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))) entries.Add(CreateFile(path));
        return entries.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public AssetEntry? GetEntry(string relativePath)
    {
        var full = Resolve(relativePath);
        return Directory.Exists(full) ? CreateDirectory(full) : File.Exists(full) ? CreateFile(full) : null;
    }

    public bool DirectoryExists(string relativeDirectory)
    {
        try { return Directory.Exists(Resolve(relativeDirectory)); }
        catch { return false; }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var root = RootOrNull(); if (root is null) return;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            { cancellationToken.ThrowIfCancellationRequested(); await EnsureMetaAsync(file, cancellationToken); }
            Changed?.Invoke();
        }
        finally { _refreshGate.Release(); }
    }

    private void StartWatching()
    {
        _watcher?.Dispose(); _watcher = null;
        var root = RootOrNull();
        if (root is null || !Directory.Exists(root)) { Changed?.Invoke(); return; }
        _watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemChanged;
        _ = RefreshAsync();
    }

    private void OnFileSystemChanged(object? sender, FileSystemEventArgs args)
    {
        if (args.FullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
        _watchDebounce?.Cancel(); _watchDebounce?.Dispose();
        var cts = _watchDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(250, cts.Token); await RefreshAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });
    }

    public async Task ImportAsync(IEnumerable<string> sourcePaths, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var target = Resolve(targetDirectory); Directory.CreateDirectory(target);
        foreach (var source in sourcePaths.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = UniquePath(Path.Combine(target, Path.GetFileName(source)));
            File.Copy(source, destination); await EnsureMetaAsync(destination, cancellationToken);
        }
        Changed?.Invoke();
    }

    public async Task MoveAsync(string relativePath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var source = Resolve(relativePath); var targetDir = Resolve(targetDirectory);
        if ((!File.Exists(source) && !Directory.Exists(source)) || !Directory.Exists(targetDir)) throw new IOException("Invalid asset move.");
        if (Directory.Exists(source) && targetDir.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("A folder cannot be moved into itself.");
        if (string.Equals(Path.GetDirectoryName(source), targetDir, StringComparison.OrdinalIgnoreCase)) return;
        var destination = UniquePath(Path.Combine(targetDir, Path.GetFileName(source)));
        if (Directory.Exists(source)) { Directory.Move(source, destination); await RewriteMetaPathsTreeAsync(destination, cancellationToken); }
        else { File.Move(source, destination); var meta = source + ".meta"; if (File.Exists(meta)) File.Move(meta, destination + ".meta"); await RewriteMetaPathAsync(destination, cancellationToken); }
        Changed?.Invoke();
    }

    public async Task RenameAsync(string relativePath, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Invalid file name.");
        var source = Resolve(relativePath); if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException();
        var destination = UniquePath(Path.Combine(Path.GetDirectoryName(source)!, newName));
        if (Directory.Exists(source)) { Directory.Move(source, destination); await RewriteMetaPathsTreeAsync(destination, cancellationToken); }
        else { File.Move(source, destination); if (File.Exists(source + ".meta")) File.Move(source + ".meta", destination + ".meta"); await RewriteMetaPathAsync(destination, cancellationToken); }
        Changed?.Invoke();
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path)) { File.Delete(path); if (File.Exists(path + ".meta")) File.Delete(path + ".meta"); }
        else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Changed?.Invoke(); return Task.CompletedTask;
    }

    public async Task UpdateMetaAsync(AssetEntry entry, string? filter, string? compress, CancellationToken cancellationToken = default)
    {
        var meta = await ReadMetaAsync(entry.FullPath, cancellationToken) ?? CreateMeta(entry.FullPath);
        meta.Filter = entry.IsImage ? filter : null; meta.Compress = entry.IsImage || entry.IsVideo ? compress : null;
        await WriteMetaAsync(entry.FullPath, meta, cancellationToken); Changed?.Invoke();
    }

    private AssetEntry CreateDirectory(string fullPath) => new() { FullPath = fullPath, RelativePath = Relative(fullPath), Name = Path.GetFileName(fullPath), IsDirectory = true };
    private AssetEntry CreateFile(string fullPath)
    {
        var meta = ReadMeta(fullPath);
        return new() { FullPath = fullPath, RelativePath = Relative(fullPath), Name = Path.GetFileName(fullPath), Type = meta?.Type ?? InferType(fullPath), Id = meta?.Id, Filter = meta?.Filter, Compress = meta?.Compress, HasValidMeta = meta is not null };
    }
    private async Task EnsureMetaAsync(string fullPath, CancellationToken ct) { if (!File.Exists(fullPath + ".meta")) await WriteMetaAsync(fullPath, CreateMeta(fullPath), ct); }
    private AssetMeta CreateMeta(string fullPath)
    {
        var type = InferType(fullPath); return new AssetMeta { Id = Guid.NewGuid().ToString("N"), Type = type, Path = Relative(fullPath), Filter = type == "sprite" ? "bilinear" : null, Compress = type is "sprite" or "video" ? "none" : null };
    }
    private async Task RewriteMetaPathAsync(string path, CancellationToken ct) { var meta = await ReadMetaAsync(path, ct) ?? CreateMeta(path); meta.Path = Relative(path); await WriteMetaAsync(path, meta, ct); }
    private async Task RewriteMetaPathsTreeAsync(string directory, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            await RewriteMetaPathAsync(file, ct);
    }
    private async Task<AssetMeta?> ReadMetaAsync(string file, CancellationToken ct) { try { return File.Exists(file + ".meta") ? JsonSerializer.Deserialize<AssetMeta>(await File.ReadAllTextAsync(file + ".meta", ct)) : null; } catch { return null; } }
    private AssetMeta? ReadMeta(string file)
    {
        try
        {
            var metaPath = file + ".meta";
            return File.Exists(metaPath) ? JsonSerializer.Deserialize<AssetMeta>(File.ReadAllText(metaPath)) : null;
        }
        catch { return null; }
    }
    private Task WriteMetaAsync(string file, AssetMeta meta, CancellationToken ct) => File.WriteAllTextAsync(file + ".meta", JsonSerializer.Serialize(meta, _json), ct);
    private string? RootOrNull() => _projects.Current?.AssetsPath;
    private string Resolve(string relative)
    {
        var root = RootOrNull() ?? throw new InvalidOperationException("No project open."); var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Asset path escapes Assets."); return full;
    }
    private string Relative(string full) => Path.GetRelativePath(RootOrNull()!, full).Replace('\\', '/');
    private static string InferType(string path) { var ext = Path.GetExtension(path); return ImageExtensions.Contains(ext) ? "sprite" : AudioExtensions.Contains(ext) ? "audio" : VideoExtensions.Contains(ext) ? "video" : "unknown"; }
    private static string UniquePath(string path) { var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path); var candidate = path; var index = 1; while (File.Exists(candidate) || Directory.Exists(candidate)) candidate = Path.Combine(dir, $"{stem} {index++}{ext}"); return candidate; }
    public void Dispose()
    {
        _projects.CurrentChanged -= _projectChangedHandler;
        _watchDebounce?.Cancel(); _watchDebounce?.Dispose(); _watcher?.Dispose();
        _refreshGate.Dispose();
    }
}
