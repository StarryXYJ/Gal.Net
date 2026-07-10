using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Variable;

namespace GalNet.Editor.ViewModels;

public sealed partial class NodeInspectorPanelViewModel : ObservableObject
{
    public EditorWorkspaceViewModel Workspace { get; }

    public bool IsNodeInspectorVisible => Workspace.InspectorMode == InspectorMode.Node;
    public bool IsPreviewVariablesVisible => Workspace.InspectorMode == InspectorMode.PreviewVariables;
    public bool HasSelectedNode => Workspace.SelectedNode is not null;
    public bool IsLinearGroupSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.LinearGroup;
    public bool IsChoiceBranchSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.ChoiceBranch;
    public bool IsConditionBranchSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.ConditionBranch;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;

    public NodeInspectorPanelViewModel(EditorWorkspaceViewModel workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
    }

    [RelayCommand]
    private void OpenGroupEditor()
    {
        if (Workspace.SelectedNode is not null)
            Workspace.OpenGroupEditor(Workspace.SelectedNode);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorWorkspaceViewModel.InspectorMode)
            or nameof(EditorWorkspaceViewModel.SelectedNode)
            or nameof(EditorWorkspaceViewModel.SelectedEdge)
            or nameof(EditorWorkspaceViewModel.HasMultipleNodeSelection)
            or nameof(EditorWorkspaceViewModel.ActivePreview))
        {
            OnPropertyChanged(nameof(IsNodeInspectorVisible));
            OnPropertyChanged(nameof(IsPreviewVariablesVisible));
            OnPropertyChanged(nameof(HasSelectedNode));
            OnPropertyChanged(nameof(IsLinearGroupSelected));
            OnPropertyChanged(nameof(IsChoiceBranchSelected));
            OnPropertyChanged(nameof(IsConditionBranchSelected));
            OnPropertyChanged(nameof(ConditionSuggestions));
            OnPropertyChanged(nameof(ValidationVariables));
        }
    }

    private void OnVariableDefinitionsChanged()
    {
        OnPropertyChanged(nameof(ConditionSuggestions));
        OnPropertyChanged(nameof(ValidationVariables));
    }
}
