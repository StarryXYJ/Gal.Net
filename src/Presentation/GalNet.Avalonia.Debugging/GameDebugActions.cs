using Avalonia.Threading;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Logging;

namespace GalNet.Avalonia.Debugging;

/// <summary>Debug operations borrow the active host and use its normal save/command gate.</summary>
public sealed class GameDebugActions(AvaloniaGameSession session, GamePageViewModel gameplay, GameDebugHostOptions options)
{
    public bool CanOpenLogDirectory => options.OpenLogDirectory is not null;

    public async Task ClearPlayerStateAsync(CancellationToken cancellationToken = default)
    {
        await session.ClearPlayerStateAsync(cancellationToken);
        await options.RestartAsync(cancellationToken);
    }

    public async Task ReloadGameResourcesAsync(CancellationToken cancellationToken = default)
    {
        await session.StopAsync(cancellationToken);
        await options.RestartAsync(cancellationToken);
    }

    public Task<DebugSaveResult> SaveDebugSnapshotAsync(DebugSaveKind kind,
        bool overwriteWhenAllVisibleSlotsAreOccupied = false, CancellationToken cancellationToken = default) =>
        session.WithSnapshotAsync(async (currentSnapshot, saves, token) =>
        {
            var slots = await saves.ListSlotsAsync(token);
            var firstEmptySlot = slots.Where(slot => slot.Timestamp == default && !slot.IsCorrupt)
                .OrderBy(slot => slot.SlotIndex).FirstOrDefault();
            if (firstEmptySlot is null && !overwriteWhenAllVisibleSlotsAreOccupied)
                return DebugSaveResult.RequiresOverwrite();
            var targetSlot = firstEmptySlot?.SlotIndex
                ?? slots.OrderBy(slot => slot.SlotIndex).FirstOrDefault()?.SlotIndex
                ?? throw new InvalidOperationException("没有可用的存档槽。");
            await saves.SaveAsync(targetSlot, DebugSnapshotFactory.Create(currentSnapshot, kind), token);
            await session.RefreshSlotsAsync(token);
            await Dispatcher.UIThread.InvokeAsync(() => gameplay.StatusMessage = $"Debug saved to slot {targetSlot}.");
            GameLog.Logger.Information("Created {DebugSaveKind} debug save in slot {SlotIndex}", kind, targetSlot);
            return DebugSaveResult.Saved(targetSlot);
        }, cancellationToken);

    public void OpenLogDirectory() =>
        (options.OpenLogDirectory ?? throw new NotSupportedException("This host cannot open a log directory."))();
}
