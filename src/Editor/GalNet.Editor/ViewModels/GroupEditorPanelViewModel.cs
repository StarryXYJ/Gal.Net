using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.ViewModels;

public partial class GroupEditorPanelViewModel : ObservableObject, IUndoRedoTarget, IDisposable
{
    private readonly IGraphEditingService _graphEditingService;
    private readonly IProjectService _projects;
    private readonly IEffectCatalog _effects;
    private readonly IEntryCatalog _catalog;

    public EditorWorkspaceViewModel Workspace { get; }
    public GraphNode GroupNode => Workspace.Nodes.Single(node => node.Id == GroupId);
    public IUndoRedoHistory? UndoRedoHistory => Workspace.UndoRedoHistory;

    public string GroupId { get; }
    private readonly ObservableCollection<EntryEditorItemViewModel> _entries;
    private bool _disposed;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;
    public IAssetManager AssetManager { get; }
    public EditorShortcutService ShortcutService { get; }
    public IReadOnlyList<EntryTypeOption> EntryTypes { get; }
    public EditorRegistry FieldEditors { get; }

    [ObservableProperty]
    private EntryEditorItemViewModel? _selectedEntry;

    [ObservableProperty]
    private decimal _batchAddCount = 1;

    public GroupEditorPanelViewModel(EditorWorkspaceViewModel workspace, string groupId, IGraphEditingService graphEditingService, IProjectService projects, IAssetManager assetManager, EditorShortcutService shortcutService, IEffectCatalog effects, IEntryCatalog catalog, EditorRegistry? fieldEditors = null)
    {
        Workspace = workspace;
        GroupId = groupId;
        _entries = GroupNode.Entries;
        _graphEditingService = graphEditingService;
        _projects = projects;
        _effects = effects;
        _catalog = catalog;
        FieldEditors = fieldEditors ?? EditorFieldEditors.CreateDefault();
        AssetManager = assetManager;
        ShortcutService = shortcutService;
        EntryTypes = catalog.Definitions.Select(definition => new EntryTypeOption(definition.Type, definition.Category, $"Entry.Type.{definition.Type}")).ToArray();
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
        _entries.CollectionChanged += OnEntriesChanged;
        foreach (var entry in _entries) Attach(entry);
    }

    [RelayCommand]
    private void AddEntry()
    {
        var selectedIndex = SelectedEntry is null ? -1 : GroupNode.Entries.IndexOf(SelectedEntry);
        InsertEntries(selectedIndex >= 0 ? selectedIndex + 1 : GroupNode.Entries.Count, 1);
    }

    [RelayCommand]
    private void AppendEntry()
    {
        InsertEntries(GroupNode.Entries.Count, 1);
    }

    [RelayCommand]
    private void AppendEntries()
    {
        InsertEntries(GroupNode.Entries.Count, Math.Clamp((int)BatchAddCount, 1, 1000));
    }

    private void InsertEntries(int index, int count)
    {
        var inserted = Workspace.InsertEntriesInto(GroupNode, index, count);
        if (inserted.Count > 0)
            SelectedEntry = inserted[^1];
    }

    [RelayCommand]
    private void RemoveEntry(EntryEditorItemViewModel? entry)
    {
        if (entry is null)
            return;

        Workspace.RemoveEntryFrom(GroupNode, entry);
    }

    [RelayCommand]
    private void MoveEntryUp(EntryEditorItemViewModel? entry)
    {
        MoveEntry(entry, -1);
    }

    [RelayCommand]
    private void MoveEntryDown(EntryEditorItemViewModel? entry)
    {
        MoveEntry(entry, 1);
    }

    [RelayCommand]
    private void ReorderEntry(ReorderRequest? request)
    {
        if (request?.Item is not EntryEditorItemViewModel entry)
            return;

        Workspace.MoveEntryTo(GroupNode, entry, request.NewIndex);
    }

    private void MoveEntry(EntryEditorItemViewModel? entry, int delta)
    {
        if (entry is null)
            return;

        ReorderEntry(new ReorderRequest(entry, GroupNode.Entries.IndexOf(entry) + delta));
    }

    private void OnVariableDefinitionsChanged()
    {
        OnPropertyChanged(nameof(ConditionSuggestions));
        OnPropertyChanged(nameof(ValidationVariables));
        foreach (var entry in GroupNode.Entries)
            Configure(entry);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (EntryEditorItemViewModel entry in e.NewItems) Attach(entry);
        if (e.OldItems is not null)
            foreach (EntryEditorItemViewModel entry in e.OldItems) entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    private void Attach(EntryEditorItemViewModel entry)
    {
        entry.PropertyChanged += OnEntryPropertyChanged;
        Configure(entry);
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not EntryEditorItemViewModel entry) return;
        if (e.PropertyName == nameof(EntryEditorItemViewModel.Type)) Configure(entry, resetValues: true);
    }

    private void Configure(EntryEditorItemViewModel entry, bool resetValues = false) => entry.ConfigureParameterFields(
        _catalog,
        _projects.Current?.Settings.Speakers ?? [],
        Workspace.AllProjectVariableDefinitions.Select(variable => variable.Name).ToArray(),
        resetValues,
        _effects);

    public void CommitSpeaker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || _projects.Current is not { } project) return;
        var speaker = value.Trim();
        if (project.Settings.Speakers.Any(item => string.Equals(item, speaker, StringComparison.OrdinalIgnoreCase))) return;
        project.Settings.Speakers.Add(speaker);
        project.IsDirty = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Workspace.VariableDefinitionsChanged -= OnVariableDefinitionsChanged;
        _entries.CollectionChanged -= OnEntriesChanged;
        foreach (var entry in _entries) entry.PropertyChanged -= OnEntryPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
