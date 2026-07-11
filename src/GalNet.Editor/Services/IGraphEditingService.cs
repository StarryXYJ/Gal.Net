using System.Collections.ObjectModel;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public interface IGraphEditingService
{
    GraphNodeViewModel CreateNode(ObservableCollection<GraphNodeViewModel> nodes, GraphNodeKind kind, double x, double y);
    bool DeleteEdge(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphEdgeViewModel edge);
    bool DeleteNode(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node);
    bool Connect(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphConnectorViewModel first, GraphConnectorViewModel second);
    bool AddEntry(GraphNodeViewModel groupNode);
    bool RemoveEntry(GraphNodeViewModel groupNode, EntryEditorItemViewModel entry);
    bool MoveEntry(GraphNodeViewModel groupNode, EntryEditorItemViewModel entry, int newIndex);
    bool AddChoiceOption(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node);
    bool RemoveChoiceOption(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchOptionEditorItemViewModel option);
    bool MoveChoiceOption(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchOptionEditorItemViewModel option, int newIndex);
    bool AddCondition(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node);
    bool RemoveCondition(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchConditionEditorItemViewModel condition);
    bool MoveCondition(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges, GraphNodeViewModel node, BranchConditionEditorItemViewModel condition, int newIndex);
    void UpdateConnectorStates(ObservableCollection<GraphNodeViewModel> nodes, ObservableCollection<GraphEdgeViewModel> edges);
}
