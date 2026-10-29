using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;

namespace GalNet.Sample.Avalonia.Services;

internal sealed partial class SampleGameSessionService
{
    internal Task<SampleDebugSaveResult> SaveDebugSnapshotAsync(SampleDebugSaveKind kind,
        bool overwriteWhenAllVisibleSlotsAreOccupied, CancellationToken cancellationToken = default) =>
        WithSnapshotAsync(async (currentSnapshot, saves, token) =>
        {
            var slots = await saves.ListSlotsAsync(token);
            var firstEmptySlot = slots.Where(slot => slot.Timestamp == default && !slot.IsCorrupt)
                .OrderBy(slot => slot.SlotIndex).FirstOrDefault();
            if (firstEmptySlot is null && !overwriteWhenAllVisibleSlotsAreOccupied)
                return SampleDebugSaveResult.RequiresOverwrite();
            var targetSlot = firstEmptySlot?.SlotIndex
                ?? slots.OrderBy(slot => slot.SlotIndex).FirstOrDefault()?.SlotIndex
                ?? throw new InvalidOperationException("没有可用的存档槽。");
            var snapshot = SampleDebugSnapshotFactory.Create(currentSnapshot, kind);
            await saves.SaveAsync(targetSlot, snapshot, token);
            await RefreshSlotsAsync(token);
            await SetGameplayStatusAsync($"Debug saved to slot {targetSlot}.");
            GameLog.Logger.Information("Created {DebugSaveKind} debug save in slot {SlotIndex}", kind, targetSlot);
            return SampleDebugSaveResult.Saved(targetSlot);
        }, cancellationToken);
}
