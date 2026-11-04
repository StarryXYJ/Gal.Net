using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Editor.Models;

namespace GalNet.Editor.Services.Interfaces;

public interface IAssetCatalogService : IDisposable
{
    event Action? Changed;
    IReadOnlyList<AssetEntry> GetDirectory(string relativeDirectory);
    bool DirectoryExists(string relativeDirectory);
    AssetEntry? GetEntry(string relativePath);
    AssetEntry? GetProjectEntry(string projectPath) => projectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? GetEntry(projectPath[7..]) : null;
    IReadOnlyList<AssetEntry> GetProjectDirectory(string projectPath) => projectPath == "Assets" ? GetDirectory("") : projectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? GetDirectory(projectPath[7..]) : [];
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<string?> GetThumbnailPathAsync(AssetEntry entry, CancellationToken cancellationToken = default);
}
