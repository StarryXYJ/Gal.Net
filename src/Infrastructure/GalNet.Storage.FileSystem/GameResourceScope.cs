using GalNet.Assets;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.GameHost.Content;

namespace GalNet.Storage.FileSystem;

/// <summary>Owns installed content, source assets and shared media-file leases. No renderer is selected here.</summary>
public sealed class GameResourceScope : IDisposable
{
    private bool _disposed;
    private GameResourceScope(string directory, GameContent content, AssetManager assets)
    {
        GameDirectory = directory; Content = content; Assets = assets;
        MediaFiles = new AssetFileMaterializer(assets);
    }

    public string GameDirectory { get; }
    public GameContent Content { get; }
    public AssetManager Assets { get; }
    public IAssetFileMaterializer MediaFiles { get; }

    public static async Task<GameResourceScope> OpenAsync(string directory,
        IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes,
        GameContentLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        var installation = await GameInstallation.OpenAsync(directory, cancellationToken);
        var content = await installation.CreateContentProvider(resourceTypes, galleryTypes, options).LoadAsync(cancellationToken);
        var assets = new AssetManager(installation.CreateAssetProviders(resourceTypes));
        try { return new GameResourceScope(installation.RootDirectory, content, assets); }
        catch { assets.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Exception? fileError = null;
        try { MediaFiles.Dispose(); }
        catch (Exception error) { fileError = error; }
        try { Assets.Dispose(); }
        catch (Exception error) when (fileError is not null) { throw new AggregateException(fileError, error); }
        if (fileError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fileError).Throw();
    }
}
