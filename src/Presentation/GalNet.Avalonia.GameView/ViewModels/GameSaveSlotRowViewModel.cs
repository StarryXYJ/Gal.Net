using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.Services;
using Serilog;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed class GameSaveSlotRowViewModel : ObservableObject, IDisposable
{
    private global::Avalonia.Media.Imaging.Bitmap? _preview;
    private readonly byte[]? _previewBytes;
    private int _previewGeneration;
    private bool _isPreviewLoading;
    public global::Avalonia.Media.Imaging.Bitmap? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public bool IsPreviewLoading { get => _isPreviewLoading; private set => SetProperty(ref _isPreviewLoading, value); }
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
    public IAsyncRelayCommand ActivateCommand => IsSaveMode ? SaveCommand : LoadCommand;

    public GameSaveSlotRowViewModel(GameSaveSlot slot, bool isSaveMode, Func<int, Task> save, Func<int, Task> load)
        : this(slot, isSaveMode, save, load, null) { }

    public GameSaveSlotRowViewModel(GameSaveSlot slot, bool isSaveMode, Func<int, Task> save, Func<int, Task> load, Func<int, string, Task<string>>? rename, bool loadPreviewSynchronously = true)
    {
        _previewBytes = slot.PreviewImage;
        if (loadPreviewSynchronously && _previewBytes is { Length: > 0 }) Preview = DecodePreview(_previewBytes);
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
    private static global::Avalonia.Media.Imaging.Bitmap? DecodePreview(byte[] bytes)
    {
        try { using var stream = new MemoryStream(bytes); return global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 480); }
        catch (Exception) { return null; }
    }
    public async Task LoadPreviewAsync()
    {
        if (_previewBytes is not { Length: > 0 } bytes || Preview is not null || IsPreviewLoading) return;
        var generation = ++_previewGeneration;
        IsPreviewLoading = true;
        var result = await Task.Run(() => DecodePreview(bytes));
        if (generation != _previewGeneration) { result?.Dispose(); return; }
        Preview = result;
        IsPreviewLoading = false;
    }
    public void ClearPreview()
    {
        _previewGeneration++;
        IsPreviewLoading = false;
        Preview?.Dispose();
        Preview = null;
    }
    public void Dispose()
    {
        RenameCommand.PropertyChanged -= OnRenameStateChanged;
        ClearPreview();
    }
}
