using GalNet.Core.Gallery;
using GalNet.Core.Runtime;
using GalNet.Core.Settings;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Progress;
using GalNet.Runtime.Variables;

namespace GalNet.GameHost;

/// <summary>Owns player persistence services; Gallery media and rendering resources belong to the presentation.</summary>
public sealed class PlayerSession : IDisposable
{
    private readonly ISaveService _saves;
    private readonly Func<CancellationToken, Task> _clearStorage;
    private bool _disposed;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public PlayerSession(ISaveService saves, PlayerVariableService variables, IGameProgressService progress,
        IGalleryDataSource galleryDataSource, Func<CancellationToken, Task> clearStorage)
    {
        _saves = saves; Variables = variables; Progress = progress;
        GalleryDataSource = galleryDataSource; _clearStorage = clearStorage;
    }

    public PlayerVariableService Variables { get; }
    public IGameProgressService Progress { get; }
    public IGalleryDataSource GalleryDataSource { get; }

    public async Task SaveAsync(int slotIndex, GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try { await SaveCoreAsync(slotIndex, snapshot, cancellationToken); }
        finally { _writeGate.Release(); }
    }

    public async Task<int?> SaveFirstEmptyAsync(GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var slots = await _saves.ListSlotsAsync(cancellationToken);
            var empty = slots.Where(slot => slot.Timestamp == default && !slot.IsCorrupt).OrderBy(slot => slot.SlotIndex).FirstOrDefault();
            if (empty is null) return null;
            await SaveCoreAsync(empty.SlotIndex, snapshot, cancellationToken);
            return empty.SlotIndex;
        }
        finally { _writeGate.Release(); }
    }

    private async Task SaveCoreAsync(int slotIndex, GameSnapshot snapshot, CancellationToken cancellationToken)
    {
        await Progress.FlushAsync(cancellationToken);
        await _saves.SaveAsync(slotIndex, new SaveRequest { Snapshot = snapshot }, cancellationToken);
    }

    public Task<GameSnapshot?> LoadAsync(int slotIndex, CancellationToken cancellationToken) =>
        _saves.LoadAsync(slotIndex, cancellationToken);

    public Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken cancellationToken) =>
        _saves.ListSlotsAsync(cancellationToken);

    public async Task<GameSnapshot> LoadLatestAsync(CancellationToken cancellationToken = default)
    {
        var slots = await ListSlotsAsync(cancellationToken);
        var slot = slots.Where(slot => slot.Timestamp != default && !slot.IsCorrupt)
            .OrderByDescending(slot => slot.Timestamp).FirstOrDefault()
            ?? throw new InvalidOperationException("There is no valid save to continue.");
        return await LoadRequiredAsync(slot.SlotIndex, cancellationToken);
    }

    public async Task<GameSnapshot> LoadRequiredAsync(int slotIndex, CancellationToken cancellationToken = default) =>
        await LoadAsync(slotIndex, cancellationToken)
            ?? throw new InvalidOperationException($"Slot {slotIndex} is empty or invalid.");

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _clearStorage(cancellationToken);
        await Variables.ResetPlayerVariablesAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var cleanup = new ResourceCleanup();
        cleanup.Run(() => (Progress as IDisposable)?.Dispose());
        cleanup.Run(Variables.Dispose);
        cleanup.Run(() => (_saves as IDisposable)?.Dispose());
        cleanup.ThrowIfFailed();
    }
}
