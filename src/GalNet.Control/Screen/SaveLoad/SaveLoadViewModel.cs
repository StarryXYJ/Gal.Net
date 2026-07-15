using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.UI;
using GalNet.Core.Services;
using GalNet.Core.Widget;

namespace GalNet.Control.ViewModels;

public enum SaveLoadMode { Load, Save }

public sealed partial class SaveLoadViewModel : ObservableObject
{
    private readonly IGameScreenNavigator _navigator;
    private readonly ISaveService? _saves;
    private readonly Func<int, Task>? _load;
    private readonly Func<int, Task>? _save;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Func<WidgetHostViewModel>? _slotHostFactory;

    public SaveLoadMode Mode { get; }
    public ObservableCollection<WidgetHostViewModel> SlotHosts { get; } = [];
    public WidgetHostViewModel PreviousButtonHost { get; private set; } = null!;
    public WidgetHostViewModel NextButtonHost { get; private set; } = null!;
    public WidgetHostViewModel BackButtonHost { get; private set; } = null!;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PageLabel))] private int _currentPage;
    [ObservableProperty] private string? _error;
    public const int PageSize = 12;
    public int PageCount => _saves is null ? 1 : Math.Max(1, (int)Math.Ceiling(_saves.MaxSlots / (double)PageSize));
    public string PageLabel => $"{CurrentPage + 1} / {PageCount}";
    public string Title => Mode == SaveLoadMode.Load ? "Load" : "Save";

    public SaveLoadViewModel(IGameScreenNavigator navigator, ISaveService? saves, SaveLoadMode mode, Func<int, Task>? load = null, Func<int, Task>? save = null)
    { _navigator = navigator; _saves = saves; Mode = mode; _load = load; _save = save; }

    public void SetHosts(Func<WidgetHostViewModel> slotHostFactory, WidgetHostViewModel previous, WidgetHostViewModel next, WidgetHostViewModel back)
    {
        _slotHostFactory = slotHostFactory; PreviousButtonHost = previous; NextButtonHost = next; BackButtonHost = back;
        ConfigureButton(previous, "Previous", PreviousCommand); ConfigureButton(next, "Next", NextCommand); ConfigureButton(back, "Back", BackCommand);
    }

    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            SlotHosts.Clear(); Error = null;
            if (_saves is null || _slotHostFactory is null) { Error = "Save storage is not configured."; return; }
            var all = await _saves.ListSlotsAsync();
            foreach (var info in all.Skip(CurrentPage * PageSize).Take(PageSize))
            {
                var host = _slotHostFactory();
                var slot = host.RequireWidget<ISaveSlot>();
                slot.SlotIndex = info.SlotIndex; slot.Timestamp = info.Timestamp == default ? null : info.Timestamp;
                slot.Description = info.IsCorrupt ? "Corrupt save" : ""; slot.IsCorrupt = info.IsCorrupt; slot.IsEnabled = !info.IsCorrupt;
                slot.Command = new AsyncRelayCommand(() => SelectAsync(info));
                SlotHosts.Add(host);
            }
        }
        finally { _refreshGate.Release(); }
    }

    private async Task SelectAsync(SaveSlotInfo info)
    {
        if (_saves is null || info.IsCorrupt) { Error = "This save cannot be used."; return; }
        if (Mode == SaveLoadMode.Load) { if (info.Timestamp != default && _load is not null) await _load(info.SlotIndex); return; }
        if (_save is not null) await _save(info.SlotIndex);
        await RefreshAsync();
    }

    [RelayCommand] private async Task PreviousAsync() { if (CurrentPage > 0) CurrentPage--; await RefreshAsync(); }
    [RelayCommand] private async Task NextAsync() { if (CurrentPage + 1 < PageCount) CurrentPage++; await RefreshAsync(); }
    [RelayCommand] private Task BackAsync() => _navigator.GoBackAsync();

    private static void ConfigureButton(WidgetHostViewModel host, string text, System.Windows.Input.ICommand command)
    { var button = host.RequireWidget<IButtonWidget>(); button.Text = text; button.Command = command; }
}
