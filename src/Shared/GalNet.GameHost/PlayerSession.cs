using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Runtime;
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

    public Task SaveAsync(int slotIndex, GameSnapshot snapshot, CancellationToken cancellationToken) => SaveAsync(slotIndex, snapshot, null, cancellationToken);

    public async Task SaveAsync(int slotIndex, GameSnapshot snapshot, byte[]? preview, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try { await SaveCoreAsync(slotIndex, snapshot, preview, cancellationToken); }
        finally { _writeGate.Release(); }
    }

    public Task<int?> SaveFirstEmptyAsync(GameSnapshot snapshot, CancellationToken cancellationToken) => SaveFirstEmptyAsync(snapshot, null, cancellationToken);

    public async Task<int?> SaveFirstEmptyAsync(GameSnapshot snapshot, byte[]? preview, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var slots = await _saves.ListSlotsAsync(cancellationToken);
            var empty = slots.Where(slot => slot.Timestamp == default && !slot.IsCorrupt).OrderBy(slot => slot.SlotIndex).FirstOrDefault();
            if (empty is null) return null;
            await SaveCoreAsync(empty.SlotIndex, snapshot, preview, cancellationToken);
            return empty.SlotIndex;
        }
        finally { _writeGate.Release(); }
    }

    private async Task SaveCoreAsync(int slotIndex, GameSnapshot snapshot, byte[]? preview, CancellationToken cancellationToken)
    {
        await Progress.FlushAsync(cancellationToken);
        var previous = await GetSlotInfoAsync(slotIndex, cancellationToken);
        await _saves.SaveAsync(slotIndex, new SaveRequest { Snapshot = snapshot, PreviewImage = preview, Description = previous?.Description }, cancellationToken);
    }

    public bool CanRenameSlots => _saves is ISaveSlotNamingService;

    public async Task RenameSlotAsync(int slotIndex, string name, CancellationToken cancellationToken = default)
    {
        if (_saves is not ISaveSlotNamingService names) throw new NotSupportedException("This save store does not support slot names.");
        await _writeGate.WaitAsync(cancellationToken);
        try { await names.RenameAsync(slotIndex, name, cancellationToken); }
        finally { _writeGate.Release(); }
    }

    public Task<GameSnapshot?> LoadAsync(int slotIndex, CancellationToken cancellationToken) =>
        _saves.LoadAsync(slotIndex, cancellationToken);

    public Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken cancellationToken) =>
        _saves.ListSlotsAsync(cancellationToken);

    public async Task<SaveSlotInfo?> GetSlotInfoAsync(int slotIndex, CancellationToken cancellationToken = default) =>
        _saves is ISaveSlotInfoSource source ? await source.GetSlotInfoAsync(slotIndex, cancellationToken)
            : (await _saves.ListSlotsAsync(cancellationToken)).FirstOrDefault(slot => slot.SlotIndex == slotIndex);

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
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _clearStorage(cancellationToken);
            await Variables.ResetPlayerVariablesAsync(cancellationToken);
        }
        finally { _writeGate.Release(); }
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
