using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Editor.ViewModels;

public sealed class NodeGraphPanelViewModel : ObservableObject
{
    public EditorWorkspaceViewModel Workspace { get; }

    public NodeGraphPanelViewModel(EditorWorkspaceViewModel workspace)
    {
        Workspace = workspace;
    }
}
