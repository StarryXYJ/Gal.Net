using GalNet.Core.Runtime;

namespace GalNet.Core.Services;

/// <summary>Metadata for one normal or quick-save slot without loading its full snapshot.</summary>
public sealed class SaveSlotInfo
{
    public int SlotIndex { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Description { get; init; }
    public string? PreviewImage { get; init; }
    public bool IsQuickSave { get; init; }
    public bool IsCorrupt { get; init; }
}

/// <summary>Optional save presentation data paired with the runtime snapshot.</summary>
public sealed class SaveRequest
{
    public required GameSnapshot Snapshot { get; init; }
    public byte[]? PreviewImage { get; init; }
    public string? Description { get; init; }
}

/// <summary>Slot and quick-save persistence supplied by the game host.</summary>
public interface ISaveService
{
    int MaxSlots { get; }
    IReadOnlyList<SaveSlotInfo> ListSlots();
    Task SaveAsync(int slot, GameSnapshot snapshot);
    Task<GameSnapshot?> LoadAsync(int slot);
    Task DeleteAsync(int slot);
    Task QuickSaveAsync(GameSnapshot snapshot);
    Task<GameSnapshot?> QuickLoadAsync();
    Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken ct = default);
    Task<SaveSlotInfo?> GetQuickSaveInfoAsync(CancellationToken ct = default);
    Task<bool> HasQuickSaveAsync(CancellationToken ct = default);
    Task DeleteQuickSaveAsync(CancellationToken ct = default);
    /// <summary>Saves a slot with optional preview and user-facing description.</summary>
    Task SaveAsync(int slot, SaveRequest request, CancellationToken ct = default);
    /// <summary>Updates the host's dedicated quick-save using optional presentation metadata.</summary>
    Task QuickSaveAsync(SaveRequest request, CancellationToken ct = default);
}
