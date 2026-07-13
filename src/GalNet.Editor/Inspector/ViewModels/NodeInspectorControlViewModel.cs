using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed partial class NodeInspectorControlViewModel : ObservableObject, IInspectorControlViewModel
{
    private readonly IEditorLocalizationService _localization;
    public EditorWorkspaceViewModel Workspace { get; }
    public bool IsAvailable => Workspace.SelectedEdge is null;
    public bool HasSelectedNode => Workspace.SelectedNode is not null;
    public bool IsLinearGroupSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.LinearGroup;
    public bool IsChoiceBranchSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.ChoiceBranch;
    public bool IsConditionBranchSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.ConditionBranch;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;
    public string EntryCountText => _localization.Format("Inspector.Node.EntryCount", Workspace.SelectedNode?.EntryCount ?? 0);

    public NodeInspectorControlViewModel(EditorWorkspaceViewModel workspace, IEditorLocalizationService localization)
    {
        Workspace = workspace;
        _localization = localization;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
        _localization.PropertyChanged += OnLocalizationPropertyChanged;
    }

    [RelayCommand] private void OpenGroupEditor()
    {
        if (Workspace.SelectedNode is not null) Workspace.OpenGroupEditor(Workspace.SelectedNode);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorWorkspaceViewModel.SelectedNode) or nameof(EditorWorkspaceViewModel.SelectedEdge) or nameof(EditorWorkspaceViewModel.HasMultipleNodeSelection))
        {
            OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(HasSelectedNode));
            OnPropertyChanged(nameof(IsLinearGroupSelected)); OnPropertyChanged(nameof(IsChoiceBranchSelected)); OnPropertyChanged(nameof(IsConditionBranchSelected));
            OnPropertyChanged(nameof(EntryCountText));
        }
    }
    private void OnVariableDefinitionsChanged() { OnPropertyChanged(nameof(ConditionSuggestions)); OnPropertyChanged(nameof(ValidationVariables)); }
    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]") OnPropertyChanged(nameof(EntryCountText)); }
    public void Dispose() { Workspace.PropertyChanged -= OnWorkspacePropertyChanged; Workspace.VariableDefinitionsChanged -= OnVariableDefinitionsChanged; _localization.PropertyChanged -= OnLocalizationPropertyChanged; }
}
