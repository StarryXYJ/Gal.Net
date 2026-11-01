using GalNet.Avalonia.GameView.Services;
using GalNet.GameHost;
using GalNet.Runtime.Logging;
using GalNet.Storage.FileSystem;
using GalNet.Presentation.Abstractions.Application;

namespace GalNet.Avalonia.GameHost;

public partial class AvaloniaGameSession
{
    // One awaited background pipeline owns every partially prepared resource until publication on the UI thread.
    private Task<(GameResourceScope Resources, AvaloniaProjectOptions Options, AvaloniaGameResources Visuals,
        PlayerSession Player, IGalleryResourceResolver Gallery, string ProfileDirectory)> PrepareResourcesAsync(string gameDirectory, string? profileDirectory,
        CancellationToken cancellationToken) => Task.Run(async () =>
    {
        GameResourceScope? resources = null;
        AvaloniaGameResources? visuals = null;
        PlayerSession? player = null;
        IGalleryResourceResolver? gallery = null;
        try
        {
            _options.LaunchPolicy.ValidateContent(gameDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            var catalogs = _options.CreateContentCatalogs();
            resources = await GameResourceScope.OpenAsync(gameDirectory,
                catalogs.Resources, catalogs.Gallery, _options.Content, cancellationToken).ConfigureAwait(false);
            resources.Assets.Diagnostic += diagnostic => GameLog.Logger.Warning(diagnostic.Exception,
                "Asset {AssetStage} failed ({AssetFailure}): ID={AssetId}, Path={AssetPath}, Provider={AssetProvider}, SourceID={SourceAssetId}, Target={AssetTargetType}",
                diagnostic.Stage, diagnostic.Kind, diagnostic.AssetId, diagnostic.Path, diagnostic.Provider, diagnostic.SourceAssetId, diagnostic.TargetType?.FullName);
            var options = _options.SelectProjectOptions(resources.Content.Configuration);
            profileDirectory ??= Path.Combine(resources.GameDirectory, ".galnet");
            _options.ConfigureDecoders(resources.Assets);
            visuals = await AvaloniaGameResources.CreateAsync(resources.Assets,
                _options.SelectTexturePreloads, _options.SelectEffectPrograms, cancellationToken).ConfigureAwait(false);
            player = await FilePlayerSession.OpenAsync(profileDirectory, resources.Content.Gallery,
                options.SaveSlotCount, cancellationToken).ConfigureAwait(false);
            gallery = await AssetGalleryResourceResolver.CreateAsync(resources.Assets,
                resources.Content.Gallery, cancellationToken, resources.MediaFiles).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return (resources, options, visuals, player, gallery, profileDirectory);
        }
        catch (Exception error)
        {
            var cleanup = new ResourceCleanup();
            cleanup.Run(() => (gallery as IDisposable)?.Dispose());
            cleanup.Run(() => player?.Dispose());
            cleanup.Run(() => visuals?.Dispose());
            cleanup.Run(() => resources?.Dispose());
            cleanup.ThrowIfFailed(error);
            throw;
        }
    }, cancellationToken);
}
