using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Runtime.Content;
using GalNet.Runtime.Gallery;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Variables;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.Services;

internal sealed class SampleSaveSession : IDisposable
{
    private readonly FileSaveService _saves;
    private bool _disposed;

    internal SampleSaveSession(
        FileSaveService saves,
        PlayerVariableService variables,
        FileGameProgressService progress,
        IGalleryDataSource galleryDataSource,
        IGalleryResourceResolver galleryResources)
    {
        _saves = saves;
        Variables = variables;
        Progress = progress;
        GalleryDataSource = galleryDataSource;
        GalleryResources = galleryResources;
    }

    public PlayerVariableService Variables { get; }
    public FileGameProgressService Progress { get; }
    public GameSettings Settings { get; private set; } = new();
    public IGalleryDataSource GalleryDataSource { get; }
    public IGalleryResourceResolver GalleryResources { get; }

    public static async Task<SampleSaveSession> CreateAsync(
        string profileDirectory,
        GameContent content,
        GalNet.Core.Assets.IAssetManager assets,
        CancellationToken cancellationToken, GalNet.Core.Assets.IAssetFileMaterializer? files = null)
    {
        var saves = new FileSaveService(profileDirectory);
        PlayerVariableService? variables = null;
        IGalleryResourceResolver? galleryResources = null;
        try
        {
            variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(profileDirectory), cancellationToken);
            variables.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(content.Gallery));
            galleryResources = await AssetGalleryResourceResolver.CreateAsync(
                assets,
                content.Gallery,
                cancellationToken, files);
            return new SampleSaveSession(
                saves,
                variables,
                new FileGameProgressService(profileDirectory),
                new GalleryDataSource(content.Gallery, variables),
                galleryResources);
        }
        catch (Exception error)
        {
            var cleanup = new SampleCleanup();
            cleanup.Run(() => (galleryResources as IDisposable)?.Dispose());
            cleanup.Run(() => variables?.Dispose());
            cleanup.Run(saves.Dispose);
            cleanup.ThrowIfFailed(error);
            throw;
        }
    }

    public async Task SaveAsync(int slotIndex, GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await Progress.FlushAsync(cancellationToken);
        await _saves.SaveAsync(slotIndex, new SaveRequest { Snapshot = snapshot }, cancellationToken);
    }

    public Task<GameSnapshot?> LoadAsync(int slotIndex, CancellationToken cancellationToken) =>
        _saves.LoadAsync(slotIndex, cancellationToken);

    public Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken cancellationToken) =>
        _saves.ListSlotsAsync(cancellationToken);

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _saves.ClearAsync(cancellationToken);
        await Variables.ResetPlayerVariablesAsync(cancellationToken);
        Progress.Clear();
        Settings = new GameSettings();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var cleanup = new SampleCleanup();
        cleanup.Run(() => (GalleryResources as IDisposable)?.Dispose());
        cleanup.Run(Progress.Dispose);
        cleanup.Run(Variables.Dispose);
        cleanup.Run(_saves.Dispose);
        cleanup.ThrowIfFailed();
    }
}
