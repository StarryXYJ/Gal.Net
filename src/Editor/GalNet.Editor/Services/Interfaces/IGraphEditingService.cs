using System.Collections.Generic;
using System.Collections.ObjectModel;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services.Interfaces;

public interface IGraphEditingService
{
    IReadOnlyList<EntryEditorItemViewModel> InsertEntries(GraphNode groupNode, int index, int count);
    bool RemoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry);
    bool MoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex);
    bool AddChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node);
    bool RemoveChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchOptionEditorItemViewModel choiceOption);
    bool MoveChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchOptionEditorItemViewModel choiceOption, int newIndex);
    bool AddCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node);
    bool RemoveCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchConditionEditorItemViewModel condition);
    bool MoveCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchConditionEditorItemViewModel condition, int newIndex);
    void UpdateConnectorStates(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges);
}
