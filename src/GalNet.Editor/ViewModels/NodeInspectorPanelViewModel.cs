using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    public NodeInspectorPanelViewModel(EditorWorkspaceViewModel workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
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
        }
    }
}
