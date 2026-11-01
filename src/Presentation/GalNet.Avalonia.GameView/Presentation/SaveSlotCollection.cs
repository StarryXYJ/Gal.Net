using System.Collections.ObjectModel;
using System.Collections.Specialized;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Owns save rows and decoded previews; keeps unaffected rows alive on single-slot updates.</summary>
public sealed class SaveSlotCollection : IDisposable
{
    private readonly IGameSessionService _session;
    private readonly Func<int, Task> _save;
    private readonly Func<int, Task> _load;
    private bool _isSaveMode;
    public ObservableCollection<GameSaveSlotRowViewModel> Slots { get; } = [];
    public SaveSlotCollection(IGameSessionService session, Func<int, Task> save, Func<int, Task> load)
    {
        _session = session; _save = save; _load = load;
        ((INotifyCollectionChanged)session.SaveSlots).CollectionChanged += OnSlotsChanged;
    }
    public void Refresh(bool isSaveMode) { _isSaveMode = isSaveMode; RebuildRows(); }
    public void Dispose()
    {
        ((INotifyCollectionChanged)_session.SaveSlots).CollectionChanged -= OnSlotsChanged;
        foreach (var row in Slots) row.Dispose();
        Slots.Clear();
    }
    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex >= 0 && e.NewItems is { Count: 1 }
            && e.NewItems[0] is GameSaveSlot replacement && e.NewStartingIndex < Slots.Count)
        {
            if (e.OldItems?[0] is GameSaveSlot previous && replacement with { Description = previous.Description } == previous)
                Slots[e.NewStartingIndex].UpdateDescription(replacement.Description);
            else
            {
                var old = Slots[e.NewStartingIndex];
                Slots[e.NewStartingIndex] = CreateRow(replacement);
                old.Dispose();
            }
            return;
        }
        RebuildRows();
    }
    private GameSaveSlotRowViewModel CreateRow(GameSaveSlot slot) => new(
        slot, _isSaveMode, _save,
        slotIndex => _load(slotIndex),
        _session is ISaveSlotNamingSession { CanRenameSlots: true } naming
            ? (slotIndex, name) => naming.RenameSlotAsync(slotIndex, name) : null);
    private void RebuildRows()
    {
        foreach (var row in Slots) row.Dispose();
        Slots.Clear();
        foreach (var slot in _session.SaveSlots)
        {
            var row = CreateRow(slot);
            if (_isSaveMode)
                row.LoadCommand.NotifyCanExecuteChanged();
            Slots.Add(row);
        }
    }
}
