using System.ComponentModel;
using Avalonia.Controls;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

/// <summary>The same scene-target selector is used by fields, Timeline and future author tools.</summary>
public sealed class SceneTargetPicker : UserControl, IDisposable
{
    private bool _refreshing;
    private SceneTargetSelection? _selection;
    public ResourcePickerPanel ResourcePanel { get; } = new();
    public event Action? SelectionChanged;
    public event Action? Confirmed;
    public event Action? Cancelled;
    public SceneTargetPicker()
    {
        Content = ResourcePanel;
        ResourcePanel.SelectionChanged += () =>
        {
            if (_refreshing || _selection is null) return;
            if (ResourcePanel.Selected is not { } item) { _selection.Clear(); SelectionChanged?.Invoke(); return; }
            if (_selection.Select(item.Id)) SelectionChanged?.Invoke(); else ShowStale();
        };
        ResourcePanel.Confirmed += () => { if (_selection?.TryResolve(out _) == true) Confirmed?.Invoke(); else ShowStale(); };
        ResourcePanel.Cancelled += () => Cancelled?.Invoke();
    }
    public void Configure(SceneTargetSelection selection)
    {
        Dispose(); _selection = selection; selection.Text.PropertyChanged += OnLocale; Refresh();
    }
    public void Refresh()
    {
        if (_selection is null) return;
        _refreshing = true;
        try
        {
            _selection.RefreshLabels();
            ResourcePanel.Search.PlaceholderText = _selection.Text[_selection.Snapshot.Playback ? "Scene.SearchPlayback" : "Scene.Search"];
            ResourcePanel.Hint = _selection.Text[_selection.IsCurrent ? _selection.Snapshot.Complete ? _selection.Snapshot.Playback ? "Scene.ChoosePlayback" : "Scene.ChooseTarget" : "Scene.CandidateHint" : "Scene.Stale"];
            ResourcePanel.SetItems(_selection.Items, _selection.SelectedId);
        }
        finally { _refreshing = false; }
    }
    public void ShowStale() { if (_selection is not null) ResourcePanel.Hint = _selection.Text["Scene.Stale"]; }
    private void OnLocale(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "Item[]") Refresh(); }
    public void Dispose() { if (_selection is not null) _selection.Text.PropertyChanged -= OnLocale; _selection = null; }
}
