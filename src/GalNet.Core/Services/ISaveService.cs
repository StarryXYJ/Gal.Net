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
}
