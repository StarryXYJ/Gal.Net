using GalNet.Editor.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GalNet.Editor.Services.Interfaces;

public interface IAssetCatalogService : IDisposable
{
    event Action? Changed;
    IReadOnlyList<AssetEntry> GetDirectory(string relativeDirectory);
    bool DirectoryExists(string relativeDirectory);
    AssetEntry? GetEntry(string relativePath);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task ImportAsync(IEnumerable<string> sourcePaths, string targetDirectory, CancellationToken cancellationToken = default);
    Task ImportExternalAsync(IEnumerable<string> sourcePaths, string targetDirectory, CancellationToken cancellationToken = default);
    Task<string?> GetThumbnailPathAsync(AssetEntry entry, CancellationToken cancellationToken = default);
    Task MoveAsync(string relativePath, string targetDirectory, CancellationToken cancellationToken = default);
    Task RenameAsync(string relativePath, string newName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
    Task UpdateMetaAsync(AssetEntry entry, string? filter, string? compress, CancellationToken cancellationToken = default);
    Task CreateDirectoryAsync(string parentDirectory, string name, CancellationToken cancellationToken = default);
}
