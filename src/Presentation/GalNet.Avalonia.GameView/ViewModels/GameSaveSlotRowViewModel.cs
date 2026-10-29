using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed class GameSaveSlotRowViewModel : IDisposable
{
    public global::Avalonia.Media.Imaging.Bitmap? Preview { get; }
    public int SlotIndex { get; }
    public string Timestamp { get; }
    public string Description { get; }
    public bool IsEmpty { get; }
    public bool IsCorrupt { get; }
    public bool IsSaveMode { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }

    public GameSaveSlotRowViewModel(GameSaveSlot slot, bool isSaveMode, Func<int, Task> save, Func<int, Task> load)
    {
        if (slot.PreviewImage is { Length: > 0 } bytes)
        {
            try { using var stream = new MemoryStream(bytes); Preview = new global::Avalonia.Media.Imaging.Bitmap(stream); }
            catch (Exception) { /* A damaged preview must not make the save unusable. */ }
        }
        SlotIndex = slot.SlotIndex;
        Timestamp = slot.DisplayTimestamp;
        Description = slot.Description;
        IsEmpty = slot.IsEmpty;
        IsCorrupt = slot.IsCorrupt;
        IsSaveMode = isSaveMode;
        SaveCommand = new AsyncRelayCommand(() => save(SlotIndex));
        LoadCommand = new AsyncRelayCommand(() => load(SlotIndex), () => !IsEmpty && !IsCorrupt);
    }
    public void Dispose() => Preview?.Dispose();
}
