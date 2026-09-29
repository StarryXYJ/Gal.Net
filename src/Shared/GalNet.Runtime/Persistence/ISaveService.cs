using GalNet.Core.Runtime;

namespace GalNet.Runtime.Persistence;

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
    Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken ct = default);
    Task SaveAsync(int slot, SaveRequest request, CancellationToken ct = default);
    Task<GameSnapshot?> LoadAsync(int slot, CancellationToken ct = default);
    Task DeleteAsync(int slot, CancellationToken ct = default);
    Task QuickSaveAsync(SaveRequest request, CancellationToken ct = default);
    Task<GameSnapshot?> QuickLoadAsync(CancellationToken ct = default);
    Task<SaveSlotInfo?> GetQuickSaveInfoAsync(CancellationToken ct = default);
    Task<bool> HasQuickSaveAsync(CancellationToken ct = default);
    Task DeleteQuickSaveAsync(CancellationToken ct = default);
}
