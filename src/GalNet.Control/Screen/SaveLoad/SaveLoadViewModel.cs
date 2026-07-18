using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Services;
using GalNet.Core.UI;

namespace GalNet.Control.Screen.SaveLoad;

public enum SaveLoadMode
{
    Load,
    Save
}

public sealed partial class SaveSlotItem : ObservableObject
{
    public int SlotIndex { get; init; }
    public string SlotLabel => $"SLOT {SlotIndex + 1:D2}";

    [ObservableProperty] private string _timestampLabel = "Empty slot";
    [ObservableProperty] private string _statusLabel = "No saved data";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _hasSave;
    [ObservableProperty] private IImage? _previewImage;

    public IAsyncRelayCommand? SelectCommand { get; init; }
}

public sealed partial class SaveLoadViewModel : ObservableObject
{
    public const int PageSize = 12;

    private readonly IGameScreenNavigator _navigator;
    private readonly ISaveService? _saveService;
    private readonly Func<int, Task>? _loadAction;
    private readonly Func<int, Task>? _saveAction;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public SaveLoadMode Mode { get; }
    public SaveLoadUiConfiguration Configuration { get; }
    public ObservableCollection<SaveSlotItem> Slots { get; } = [];
    public int TotalSlotCount => _saveService?.MaxSlots ?? PageSize;
    public string Title => Mode == SaveLoadMode.Load ? "Load Game" : "Save Game";

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private string? _error;

    public SaveLoadViewModel(
        IGameScreenNavigator navigator,
        ISaveService? saveService,
        SaveLoadMode mode,
        SaveLoadUiConfiguration configuration,
        Func<int, Task>? loadAction = null,
        Func<int, Task>? saveAction = null)
    {
        _navigator = navigator;
        _saveService = saveService;
        _loadAction = loadAction;
        _saveAction = saveAction;
        Mode = mode;
        Configuration = configuration;
    }

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            Slots.Clear();
            if (_saveService is null)
            {
                Error = "Save storage is not configured.";
                return;
            }

            Error = null;
            var offset = (CurrentPage - 1) * PageSize;
            var page = (await _saveService.ListSlotsAsync()).Skip(offset).Take(PageSize);
            foreach (var slot in page)
                Slots.Add(CreateSlot(slot));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private SaveSlotItem CreateSlot(SaveSlotInfo slot)
    {
        var hasSave = slot.Timestamp != default && !slot.IsCorrupt;
        var isSelectable = !slot.IsCorrupt && (Mode == SaveLoadMode.Save || hasSave);
        var item = new SaveSlotItem
        {
            SlotIndex = slot.SlotIndex,
            TimestampLabel = hasSave ? slot.Timestamp.ToString("yyyy/MM/dd  HH:mm") : slot.IsCorrupt ? "Unreadable data" : "Empty slot",
            StatusLabel = hasSave ? string.Empty : slot.IsCorrupt ? "CORRUPTED" : "EMPTY",
            IsEnabled = isSelectable,
            HasSave = hasSave,
            SelectCommand = new AsyncRelayCommand(() => SelectAsync(slot))
        };

        if (!string.IsNullOrWhiteSpace(slot.PreviewImage) && File.Exists(slot.PreviewImage))
        {
            try
            {
                item.PreviewImage = new Bitmap(slot.PreviewImage);
                item.HasPreview = true;
            }
            catch
            {
                // A broken preview must not make an otherwise valid save unreadable.
            }
        }

        return item;
    }

    private async Task SelectAsync(SaveSlotInfo slot)
    {
        if (_saveService is null || slot.IsCorrupt)
            return;

        if (Mode == SaveLoadMode.Load)
        {
            if (_loadAction is not null && slot.Timestamp != default)
                await _loadAction(slot.SlotIndex);
            return;
        }

        if (_saveAction is not null)
        {
            await _saveAction(slot.SlotIndex);
            await RefreshAsync();
        }
    }

    partial void OnCurrentPageChanged(int value)
    {
        if (value > 0)
            _ = RefreshAsync();
    }

    [RelayCommand]
    private Task BackAsync() => _navigator.GoBackAsync();
}
