using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Services;
using GalNet.Editor.Controls;

namespace GalNet.Editor.ViewModels;

public partial class GroupEditorPanelViewModel : ObservableObject
{
    private readonly IGraphEditingService _graphEditingService;

    public EditorWorkspaceViewModel Workspace { get; }
    public GraphNode GroupNode { get; }

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
        if (_graphEditingService.AddEntry(GroupNode))
            Workspace.SaveGraphDocument();
    }

    [RelayCommand]
    private void RemoveEntry(EntryEditorItemViewModel? entry)
    {
        if (entry is null)
            return;

        if (_graphEditingService.RemoveEntry(GroupNode, entry))
            Workspace.SaveGraphDocument();
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

        if (_graphEditingService.MoveEntry(GroupNode, entry, request.NewIndex))
            Workspace.SaveGraphDocument();
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
