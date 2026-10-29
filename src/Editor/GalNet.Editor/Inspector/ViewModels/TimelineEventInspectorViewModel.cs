using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Timeline;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed partial class TimelineEventInspectorViewModel : ObservableObject, IInspectorControlViewModel, IInspectorLockAware
{
    private readonly TimelinePanelViewModel _page;
    private bool _locked, _updating, _disposed;
    public TimelineEventEdit? Draft { get; private set; }
    public string Label { get; private set; } = "";
    public bool IsAvailable => Draft is { IsCurrent: true };
    public GalNet.Game.Controls.Forms.EditorRegistry FieldEditors => _page.FieldEditors;
    public TimelineEventInspectorViewModel(TimelinePanelViewModel page)
    {
        _page = page;
        page.Session.EventCanvas.PropertyChanged += SelectionChanged;
        page.Session.PropertyChanged += SessionChanged;
        Sync();
    }
    private void SelectionChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(TimelineCanvasModel.Selection) && !_locked) Sync(); }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    { if (Draft is not null && !Draft.IsCurrent) { Draft.Dispose(); Draft = null; Notify(); } }
    private void Sync()
    {
        if (_updating || _disposed) return;
        _updating = true;
        try
        {
            var rows = _page.Session.Events.Where(row => row.IsSelected).ToArray();
            var row = rows.Length == 1 && rows[0].CanEdit ? rows[0] : null;
            Label = row?.Label ?? "";
            if (row is null || Draft is not { IsCurrent: true } draft || draft.Index != row.Index)
            {
                Draft?.Dispose();
                Draft = row is null ? null : _page.Session.CreateInspectorEventEdit(row);
            }
            Notify();
        }
        finally { _updating = false; }
    }
    private void Notify() { OnPropertyChanged(nameof(Draft)); OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(IsAvailable)); }
    [RelayCommand] private void Apply() { if (Draft?.Apply() == true) { Draft?.Dispose(); Draft = null; if (!_locked) Sync(); else Notify(); } }
    [RelayCommand] private void Cancel() { Draft?.Dispose(); Draft = null; if (!_locked) Sync(); else Notify(); }
    public void SetLocked(bool isLocked) { _locked = isLocked; if (!isLocked) Sync(); }
    public void Dispose()
    {
        _disposed = true;
        _page.Session.EventCanvas.PropertyChanged -= SelectionChanged;
        _page.Session.PropertyChanged -= SessionChanged;
        Draft?.Dispose(); Draft = null;
    }
}
