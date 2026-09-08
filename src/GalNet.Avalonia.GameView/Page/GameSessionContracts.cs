using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Host-owned session operations consumed by the default page VMs.</summary>
public interface IGameSessionService : INotifyPropertyChanged
{
    string GameTitle { get; }
    string StatusMessage { get; }
    bool IsReady { get; }
    ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default);
    Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default);
}

/// <summary>Presentation-safe save slot data. The host owns loading, writing and preview bytes.</summary>
public sealed record GameSaveSlot(int SlotIndex, string Timestamp, string Description, bool IsEmpty, bool IsCorrupt);
