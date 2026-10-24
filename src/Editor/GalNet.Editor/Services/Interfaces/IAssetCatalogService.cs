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
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<string?> GetThumbnailPathAsync(AssetEntry entry, CancellationToken cancellationToken = default);
}
