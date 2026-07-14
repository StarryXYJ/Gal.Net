using GalNet.Core.Runtime;

namespace GalNet.Core.Services;

/// <summary>
/// Info about a single save slot.
/// </summary>
public sealed class SaveSlotInfo
{
    public int SlotIndex { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Description { get; init; }
    public string? PreviewImage { get; init; }
    public bool IsQuickSave { get; init; }
    public bool IsCorrupt { get; init; }
}

/// <summary>Data written alongside a snapshot. PreviewImage is a PNG byte array.</summary>
public sealed class SaveRequest
{
    public required GameSnapshot Snapshot { get; init; }
    public byte[]? PreviewImage { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Save/Load service — manages save slots and persistence.
/// </summary>
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
    Task SaveAsync(int slot, SaveRequest request, CancellationToken ct = default);
    Task QuickSaveAsync(SaveRequest request, CancellationToken ct = default);
}
