using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GalNet.Editor.ViewModels;

public partial class GroupEditorPanelViewModel : ObservableObject
{
    public EditorWorkspaceViewModel Workspace { get; }
    public GraphNodeViewModel GroupNode { get; }

    public string GroupId => GroupNode.Id;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;

    public GroupEditorPanelViewModel(EditorWorkspaceViewModel workspace, GraphNodeViewModel groupNode)
    {
        Workspace = workspace;
        GroupNode = groupNode;
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
    }

    [RelayCommand]
    private void AddEntry()
    {
        GroupNode.Entries.Add(new EntryEditorItemViewModel
        {
            Id = GroupNode.Entries.Count + 1,
            Type = "text",
            Parameters = "speaker=; text="
        });
    }

    [RelayCommand]
    private void RemoveEntry(EntryEditorItemViewModel? entry)
    {
        if (entry is null)
            return;

        GroupNode.Entries.Remove(entry);
        RenumberEntries();
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

    public void MoveEntryTo(EntryEditorItemViewModel? entry, int newIndex)
    {
        if (entry is null)
            return;

        var oldIndex = GroupNode.Entries.IndexOf(entry);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= GroupNode.Entries.Count)
            return;

        GroupNode.Entries.Move(oldIndex, newIndex);
        RenumberEntries();
    }

    private void MoveEntry(EntryEditorItemViewModel? entry, int delta)
    {
        if (entry is null)
            return;

        MoveEntryTo(entry, GroupNode.Entries.IndexOf(entry) + delta);
    }

    private void RenumberEntries()
    {
        for (var i = 0; i < GroupNode.Entries.Count; i++)
            GroupNode.Entries[i].Id = i + 1;
    }

    private void OnVariableDefinitionsChanged()
    {
        OnPropertyChanged(nameof(ConditionSuggestions));
        OnPropertyChanged(nameof(ValidationVariables));
    }
}
