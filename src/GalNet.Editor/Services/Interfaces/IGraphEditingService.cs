using System.Collections.ObjectModel;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public interface IGraphEditingService
{
    GraphNode CreateNode(ObservableCollection<GraphNode> nodes, GraphNodeKind kind, double x, double y);
    bool DeleteEdge(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphEdge edge);
    bool DeleteNode(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node);
    bool Connect(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphConnector first, GraphConnector second);
    bool AddEntry(GraphNode groupNode);
    bool RemoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry);
    bool MoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex);
    bool AddChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node);
    bool RemoveChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchOptionEditorItemViewModel option);
    bool MoveChoiceOption(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchOptionEditorItemViewModel option, int newIndex);
    bool AddCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node);
    bool RemoveCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchConditionEditorItemViewModel condition);
    bool MoveCondition(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges, GraphNode node, BranchConditionEditorItemViewModel condition, int newIndex);
    void UpdateConnectorStates(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges);
}
