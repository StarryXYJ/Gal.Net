using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Inspector.ViewModels;

/// <summary>Inspector state is a snapshot so a locked inspector remains on its original node.</summary>
public sealed partial class NodeInspectorControlViewModel : ObservableObject, IInspectorControlViewModel, IInspectorLockAware
{
    private readonly IEditorLocalizationService _localization;
    private bool _isLocked;

    public EditorWorkspaceViewModel Workspace { get; }
    [ObservableProperty] private GraphNode? _inspectedNode;
    [ObservableProperty] private GraphEdge? _inspectedEdge;

    public bool IsAvailable => InspectedEdge is null;
    public bool HasSelectedNode => InspectedNode is not null;
    public bool IsLinearGroupSelected => InspectedNode?.NodeKind == GraphNodeKind.LinearGroup;
    public bool IsChoiceBranchSelected => InspectedNode?.NodeKind == GraphNodeKind.ChoiceBranch;
    public bool IsConditionBranchSelected => InspectedNode?.NodeKind == GraphNodeKind.ConditionBranch;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;
    public string EntryCountText => _localization.Format("Inspector.Node.EntryCount", InspectedNode?.EntryCount ?? 0);

    public NodeInspectorControlViewModel(EditorWorkspaceViewModel workspace, IEditorLocalizationService localization)
    {
        Workspace = workspace;
        _localization = localization;
        SyncSelection();
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
        _localization.PropertyChanged += OnLocalizationPropertyChanged;
    }

    public void SetLocked(bool isLocked)
    {
        _isLocked = isLocked;
        if (!isLocked)
            SyncSelection();
    }

    [RelayCommand]
    private void OpenGroupEditor()
    {
        if (InspectedNode is not null)
            Workspace.OpenGroupEditor(InspectedNode);
    }

    [RelayCommand] private void AddChoiceOption() => Workspace.AddChoiceOptionTo(InspectedNode);
    [RelayCommand] private void RemoveChoiceOption(BranchOptionEditorItemViewModel? option) => Workspace.RemoveChoiceOptionFrom(InspectedNode, option);
    [RelayCommand] private void ReorderChoiceOption(ReorderRequest? request)
    {
        if (request?.Item is BranchOptionEditorItemViewModel option)
            Workspace.MoveChoiceOptionTo(InspectedNode, option, request.NewIndex);
    }

    [RelayCommand] private void AddCondition() => Workspace.AddConditionTo(InspectedNode);
    [RelayCommand] private void RemoveCondition(BranchConditionEditorItemViewModel? condition) => Workspace.RemoveConditionFrom(InspectedNode, condition);
    [RelayCommand] private void ReorderCondition(ReorderRequest? request)
    {
        if (request?.Item is BranchConditionEditorItemViewModel condition)
            Workspace.MoveConditionTo(InspectedNode, condition, request.NewIndex);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isLocked && e.PropertyName is nameof(EditorWorkspaceViewModel.SelectedNode) or nameof(EditorWorkspaceViewModel.SelectedEdge))
            SyncSelection();
    }

    private void SyncSelection()
    {
        InspectedNode = Workspace.SelectedNode;
        InspectedEdge = Workspace.SelectedEdge;
        RaiseSelectionPropertiesChanged();
    }

    private void RaiseSelectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(IsLinearGroupSelected));
        OnPropertyChanged(nameof(IsChoiceBranchSelected));
        OnPropertyChanged(nameof(IsConditionBranchSelected));
        OnPropertyChanged(nameof(EntryCountText));
    }

    private void OnVariableDefinitionsChanged()
    {
        OnPropertyChanged(nameof(ConditionSuggestions));
        OnPropertyChanged(nameof(ValidationVariables));
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]")
            OnPropertyChanged(nameof(EntryCountText));
    }

    public void Dispose()
    {
        Workspace.PropertyChanged -= OnWorkspacePropertyChanged;
        Workspace.VariableDefinitionsChanged -= OnVariableDefinitionsChanged;
        _localization.PropertyChanged -= OnLocalizationPropertyChanged;
    }
}
