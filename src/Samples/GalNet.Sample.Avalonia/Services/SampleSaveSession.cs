using GalNet.Avalonia.GameView.Services;
using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Runtime.Content;
using GalNet.Runtime.Gallery;
using GalNet.Runtime.Persistence;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.Services;

internal sealed class SampleSaveSession : IDisposable
{
    private readonly FileSaveService _saves;
    private bool _disposed;

    private SampleSaveSession(
        FileSaveService saves,
        FileVariableService variables,
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

    public FileVariableService Variables { get; }
    public FileGameProgressService Progress { get; }
    public GameSettings Settings { get; private set; } = new();
    public IGalleryDataSource GalleryDataSource { get; }
    public IGalleryResourceResolver GalleryResources { get; }

    public static async Task<SampleSaveSession> CreateAsync(
        string profileDirectory,
        GameContent content,
        GalNet.Core.Assets.IAssetManager assets,
        CancellationToken cancellationToken)
    {
        var saves = new FileSaveService(profileDirectory);
        var variables = await FileVariableService.CreateAsync(
            new FilePlayerVariableStore(profileDirectory),
            cancellationToken);
        variables.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(content.Gallery));
        IGalleryResourceResolver? galleryResources = null;
        try
        {
            galleryResources = await AssetGalleryResourceResolver.CreateAsync(
                assets,
                content.Gallery,
                cancellationToken);
            return new SampleSaveSession(
                saves,
                variables,
                new FileGameProgressService(profileDirectory),
                new GalleryDataSource(content.Gallery, variables),
                galleryResources);
        }
        catch
        {
            (galleryResources as IDisposable)?.Dispose();
            throw;
        }
    }

    public Task SaveAsync(int slotIndex, GameSnapshot snapshot, CancellationToken cancellationToken) =>
        _saves.SaveAsync(slotIndex, new SaveRequest { Snapshot = snapshot }, cancellationToken);

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
        (GalleryResources as IDisposable)?.Dispose();
    }
}
