using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed class GameSaveSlotRowViewModel
{
    public int SlotIndex { get; }
    public string Timestamp { get; }
    public string Description { get; }
    public bool IsEmpty { get; }
    public bool IsCorrupt { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }

    public GameSaveSlotRowViewModel(GameSaveSlot slot, Func<int, Task> save, Func<int, Task> load)
    {
        SlotIndex = slot.SlotIndex;
        Timestamp = slot.Timestamp;
        Description = slot.Description;
        IsEmpty = slot.IsEmpty;
        IsCorrupt = slot.IsCorrupt;
        SaveCommand = new AsyncRelayCommand(() => save(SlotIndex));
        LoadCommand = new AsyncRelayCommand(() => load(SlotIndex), () => !IsEmpty && !IsCorrupt);
    }
}
