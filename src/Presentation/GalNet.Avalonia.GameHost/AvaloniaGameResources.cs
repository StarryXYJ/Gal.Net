using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Rendering.Scene;

namespace GalNet.Avalonia.GameHost;

/// <summary>Renderer-specific preloads. Borrows the asset manager; releases its leases before the source scope.</summary>
public sealed class AvaloniaGameResources : IDisposable
{
    private readonly Dictionary<string, AssetHandle<SceneTexture>> _textures = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, AssetHandle<SceneTexture>> PreloadedTextures => _textures;
    public int SpriteFileCount { get; private set; }
    public IReadOnlyList<EffectProgramResource> EffectPrograms { get; private set; } = [];

    public static async Task<AvaloniaGameResources> CreateAsync(IAssetManager assets,
        Func<IAssetManager, CancellationToken, Task<IReadOnlyList<IGameFile>>> selectTextures,
        Func<IAssetManager, CancellationToken, Task<EffectProgramResource[]>> selectEffects,
        CancellationToken cancellationToken = default)
    {
        var result = new AvaloniaGameResources();
        try
        {
            var files = await selectTextures(assets, cancellationToken);
            result.SpriteFileCount = files.Count;
            // Record each acquired lease immediately so partial failures cannot leak successful acquisitions.
            await Task.WhenAll(files.Select(async file =>
            {
                var handle = await assets.AcquireAsync<SceneTexture>(file.Id, cancellationToken);
                if (handle is null) return;
                lock (result._textures)
                {
                    if (!result._textures.TryAdd(handle.AssetId, handle)) handle.Dispose();
                }
            }));
            result.EffectPrograms = await selectEffects(assets, cancellationToken);
            return result;
        }
        catch (Exception error)
        {
            try { result.Dispose(); }
            catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
            throw;
        }
    }

    public void Dispose()
    {
        var cleanup = new GalNet.GameHost.ResourceCleanup();
        foreach (var handle in _textures.Values) cleanup.Run(handle.Dispose);
        _textures.Clear();
        cleanup.ThrowIfFailed();
    }
}
