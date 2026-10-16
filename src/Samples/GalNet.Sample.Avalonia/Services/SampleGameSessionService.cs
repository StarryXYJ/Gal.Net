using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Media.LibVlc;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Project launch arguments and debug-only save policies.</summary>
internal sealed class SampleGameSessionService(
    GamePageViewModel gameplay, GamePage page, AvaloniaPlayerSettingsService settings,
    SampleLocalizationService localization, AvaloniaPlayerStartup startup, LibVlcRuntime mediaRuntime)
    : AvaloniaGameSession(SampleCustomization.CreateHostOptions(), gameplay, page, settings, localization, startup, mediaRuntime)
{
    public Task<bool> InitializeAsync(GameLaunchOptions options,
        Func<string, CancellationToken, Task<string?>> selectLanguage, CancellationToken cancellationToken = default) =>
        InitializeAsync(options.GameDirectory, options.ProfileDirectory, selectLanguage, cancellationToken);

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
