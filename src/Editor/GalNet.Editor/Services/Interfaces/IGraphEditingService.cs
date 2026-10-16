using System.Collections.Generic;
using System.Collections.ObjectModel;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services.Interfaces;

public interface IGraphEditingService
{
    IReadOnlyList<EntryEditorItemViewModel> InsertEntries(GraphNode groupNode, int index, int count);
    bool RemoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry);
    bool MoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex);
    void UpdateConnectorStates(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges);
}
