using System.Collections.ObjectModel;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services.Interfaces;

public interface IGraphEditingService
{
    void UpdateConnectorStates(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges);
}
