using System.Collections.ObjectModel;
using GalNet.Core.Services;

namespace GalNet.Control.ViewModels;

public enum SaveLoadMode { Load, Save }

public sealed class SaveSlotCardViewModel
{
    public required SaveSlotInfo Info { get; init; }
    public string Label => Info.IsCorrupt ? "Corrupt save" : Info.Timestamp == default ? "Empty" : Info.Timestamp.ToString("g");
}

/// <summary>12-slot paginated save/load page. The host provides the actual load callback.</summary>
public sealed class SaveLoadViewModel
{
    private readonly INavigationService _navigation;
    private readonly ISaveService? _saves;
    private readonly Func<int, Task>? _load;
    private readonly Func<int, Task>? _save;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    public SaveLoadMode Mode { get; }
    public ObservableCollection<SaveSlotCardViewModel> Slots { get; } = [];
    public int CurrentPage { get; private set; }
    public int TotalSlotCount => _saves?.MaxSlots ?? 0;
    public const int PageSize = 12;
    public int PageCount => _saves is null ? 1 : (int)Math.Ceiling(_saves.MaxSlots / (double)PageSize);
    public string Title => Mode == SaveLoadMode.Load ? "Load" : "Save";
    public string? Error { get; private set; }

    public SaveLoadViewModel(INavigationService navigation, ISaveService? saves, SaveLoadMode mode, Func<int, Task>? load = null, Func<int, Task>? save = null)
    { _navigation = navigation; _saves = saves; Mode = mode; _load = load; _save = save; }

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            Slots.Clear(); Error = null;
            if (_saves is null) { Error = "Save storage is not configured."; return; }
            var all = await _saves.ListSlotsAsync();
            foreach (var slot in all.Skip(CurrentPage * PageSize).Take(PageSize)) Slots.Add(new SaveSlotCardViewModel { Info = slot });
        }
        finally { _refreshGate.Release(); }
    }
    public async Task SelectAsync(SaveSlotCardViewModel card)
    {
        if (_saves is null || card.Info.IsCorrupt) { Error = "This save cannot be used."; return; }
        if (Mode == SaveLoadMode.Load)
        {
            if (card.Info.Timestamp == default) return;
            if (_load is not null) await _load(card.Info.SlotIndex);
            return;
        }
        if (_save is not null) await _save(card.Info.SlotIndex);
        await RefreshAsync();
    }
    public Task PreviousAsync() { if (CurrentPage > 0) CurrentPage--; return RefreshAsync(); }
    public Task NextAsync() { if (CurrentPage + 1 < PageCount) CurrentPage++; return RefreshAsync(); }
    public Task SetPageAsync(int oneBasedPage)
    {
        CurrentPage = Math.Clamp(oneBasedPage - 1, 0, Math.Max(0, PageCount - 1));
        return RefreshAsync();
    }
    public void Back() => _navigation.GoBack();
}
