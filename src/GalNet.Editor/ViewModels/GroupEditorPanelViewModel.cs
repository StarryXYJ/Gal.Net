using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;

namespace GalNet.Editor.ViewModels;

public partial class GroupEditorPanelViewModel : ObservableObject, IUndoRedoTarget
{
    private readonly IGraphEditingService _graphEditingService;

    public EditorWorkspaceViewModel Workspace { get; }
    public GraphNode GroupNode { get; }
    public IUndoRedoHistory? UndoRedoHistory => Workspace.UndoRedoHistory;

    public string GroupId => GroupNode.Id;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;

    public GroupEditorPanelViewModel(EditorWorkspaceViewModel workspace, GraphNode groupNode, IGraphEditingService graphEditingService)
    {
        Workspace = workspace;
        GroupNode = groupNode;
        _graphEditingService = graphEditingService;
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
    }

    [RelayCommand]
    private void AddEntry()
    {
        Workspace.AddEntryTo(GroupNode);
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
    }
}
