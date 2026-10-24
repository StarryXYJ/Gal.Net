using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed class EntryInspectorViewModel : ObservableObject, IInspectorControlViewModel, IInspectorLockAware, IUndoRedoTarget
{
    public GroupEditorPanelViewModel Page { get; }
    private bool _locked;
    private string? _entryId;
    private readonly AuthorNavigationService? _navigation;
    public string? RequestedField { get; private set; }
    public EntryParameterFormBinding? Form { get; private set; }
    private EntryEditorItemViewModel? _formOwner;
    private string? _formType;
    public EntryEditorItemViewModel? Entry => Page.Workspace.Nodes.FirstOrDefault(node => node.Id == Page.GroupId)?.Entries.FirstOrDefault(entry => entry.StableId == _entryId);
    public EditorRegistry FieldEditors => Page.FieldEditors;
    public bool IsAvailable => Entry is not null;
    public IUndoRedoHistory? UndoRedoHistory => Page.UndoRedoHistory;
    public string TargetDescription => Entry is { } entry ? $"{Page.Workspace.Nodes.FirstOrDefault(node => node.Id == Page.GroupId)?.Name} / {entry.Id} · {entry.Type}" : "";
    public bool HasMultipleSelection => Page.SelectedEntries.Count > 1;
    public int SelectionCount => Page.SelectedEntries.Count;
    public EntryInspectorViewModel(GroupEditorPanelViewModel page, AuthorNavigationService? navigation = null)
    {
        Page = page;
        _navigation = navigation;
        if (_navigation is not null) _navigation.Requested += Navigate;
        Page.PropertyChanged += PageChanged;
        Page.SelectedEntries.CollectionChanged += SelectionChanged;
        Page.Workspace.Nodes.CollectionChanged += NodesChanged;
        Page.Workspace.UndoRedoHistory!.Changed += Refresh;
        Sync();
    }
    private void PageChanged(object? sender, PropertyChangedEventArgs args)
    { if (!_locked && args.PropertyName == nameof(Page.SelectedEntry)) Sync(); }
    private void NodesChanged(object? sender, NotifyCollectionChangedEventArgs args) => Refresh();
    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    { OnPropertyChanged(nameof(HasMultipleSelection)); OnPropertyChanged(nameof(SelectionCount)); }
    private void Sync() { _entryId = Page.SelectedEntry?.StableId; Refresh(); }
    private void Navigate(AuthorNavigationTarget target)
    {
        if (target.NodeId != Page.GroupId || target.EntryId != _entryId) return;
        RequestedField = target.FieldId; OnPropertyChanged(nameof(RequestedField));
    }
    private void Refresh()
    {
        var entry = Entry;
        if (entry is not null) Page.EnsureParameterFields(entry);
        if (!ReferenceEquals(entry, _formOwner) || entry?.Type != _formType)
        {
            Form?.Dispose(); Form = null; _formOwner = entry; _formType = entry?.Type;
            if (entry?.DetailForm is { } form) Form = new EntryParameterFormBinding(entry, form.Fields, entry.ParameterDtoType);
            OnPropertyChanged(nameof(Form));
        }
        Form?.Refresh();
        OnPropertyChanged(nameof(Entry)); OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(TargetDescription));
    }
    public void SetLocked(bool isLocked) { _locked = isLocked; if (!_locked) Sync(); }
    public void Dispose()
    {
        Form?.Dispose();
        if (_navigation is not null) _navigation.Requested -= Navigate;
        Page.PropertyChanged -= PageChanged;
        Page.SelectedEntries.CollectionChanged -= SelectionChanged;
        Page.Workspace.Nodes.CollectionChanged -= NodesChanged;
        Page.Workspace.UndoRedoHistory!.Changed -= Refresh;
    }
}
