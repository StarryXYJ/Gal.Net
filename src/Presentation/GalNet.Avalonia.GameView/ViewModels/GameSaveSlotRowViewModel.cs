using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.Services;
using Serilog;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed class GameSaveSlotRowViewModel : ObservableObject, IDisposable
{
    public global::Avalonia.Media.Imaging.Bitmap? Preview { get; }
    public int SlotIndex { get; }
    public string Timestamp { get; }
    private string _description;
    public string Description { get => _description; private set => SetProperty(ref _description, value); }
    private string? _renameError;
    public string? RenameError { get => _renameError; private set => SetProperty(ref _renameError, value); }
    private readonly bool _canRename;
    public bool CanRename => _canRename && !RenameCommand.IsRunning;
    public IAsyncRelayCommand<string> RenameCommand { get; }
    public bool IsEmpty { get; }
    public bool IsCorrupt { get; }
    public bool IsSaveMode { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }

    public GameSaveSlotRowViewModel(GameSaveSlot slot, bool isSaveMode, Func<int, Task> save, Func<int, Task> load)
        : this(slot, isSaveMode, save, load, null) { }

    public GameSaveSlotRowViewModel(GameSaveSlot slot, bool isSaveMode, Func<int, Task> save, Func<int, Task> load, Func<int, string, Task<string>>? rename)
    {
        if (slot.PreviewImage is { Length: > 0 } bytes)
        {
            try { using var stream = new MemoryStream(bytes); Preview = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 480); }
            catch (Exception) { /* A damaged preview must not make the save unusable. */ }
        }
        SlotIndex = slot.SlotIndex;
        Timestamp = slot.DisplayTimestamp;
        _description = slot.Description;
        IsEmpty = slot.IsEmpty;
        IsCorrupt = slot.IsCorrupt;
        IsSaveMode = isSaveMode;
        _canRename = rename is not null && !IsEmpty && !IsCorrupt;
        RenameCommand = new AsyncRelayCommand<string>(async name =>
        {
            try
            {
                RenameError = null;
                Description = await rename!(SlotIndex, name ?? "");
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Failed to rename save slot {SlotIndex}", SlotIndex);
                RenameError = PlayerTextExtension.Resolve?.Invoke("SaveSlots.RenameFailed")?.ToString() ?? "Could not save the name. Try again.";
            }
        }, _ => _canRename);
        RenameCommand.PropertyChanged += OnRenameStateChanged;
        SaveCommand = new AsyncRelayCommand(async () => { await FinishRenameAsync(); if (RenameError is null) await save(SlotIndex); });
        LoadCommand = new AsyncRelayCommand(async () => { await FinishRenameAsync(); if (RenameError is null) await load(SlotIndex); }, () => !IsEmpty && !IsCorrupt);
    }
    private Task FinishRenameAsync() => RenameCommand.ExecutionTask ?? Task.CompletedTask;
    private void OnRenameStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RenameCommand.IsRunning)) OnPropertyChanged(nameof(CanRename));
    }
    internal void UpdateDescription(string description) => Description = description;
    public void Dispose()
    {
        RenameCommand.PropertyChanged -= OnRenameStateChanged;
        Preview?.Dispose();
    }
}
