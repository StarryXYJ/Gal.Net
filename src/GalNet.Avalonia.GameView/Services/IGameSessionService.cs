using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Host-owned session operations consumed by the default page VMs.</summary>
public interface IGameSessionService : INotifyPropertyChanged
{
    string GameTitle { get; }
    string StatusMessage { get; }
    bool IsReady { get; }
    bool IsPlaying { get; }
    bool CanContinue { get; }
    ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; }

    /// <summary>Creates a fresh runtime and starts the entry flow.</summary>
    Task StartNewGameAsync(CancellationToken cancellationToken = default);
    /// <summary>Restores the most recent valid slot and starts its flow.</summary>
    Task ContinueAsync(CancellationToken cancellationToken = default);
    /// <summary>Cancels and awaits any running game flow.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default);
    Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional two-phase startup contract for hosts that need a page transition to finish before
/// the game view begins presenting the first script entry. Preparation may perform I/O and
/// construct a runtime, but it must not start the engine flow.
/// </summary>
public interface IPreparedGameSessionService
{
    Task PrepareNewGameAsync(CancellationToken cancellationToken = default);
    Task PrepareContinueAsync(CancellationToken cancellationToken = default);
    Task PrepareLoadAsync(int slotIndex, CancellationToken cancellationToken = default);
    Task BeginPreparedGameAsync(CancellationToken cancellationToken = default);
}

/// <summary>Presentation-safe save slot data. The host owns loading, writing and preview bytes.</summary>
public sealed record GameSaveSlot(int SlotIndex, DateTime Timestamp, string Description, bool IsEmpty, bool IsCorrupt)
{
    public string DisplayTimestamp => Timestamp == default ? string.Empty : Timestamp.ToString("g");
}
