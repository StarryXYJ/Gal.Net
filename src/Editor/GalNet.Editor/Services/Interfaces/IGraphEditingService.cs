using System.Collections.Generic;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services.Interfaces;

public interface IGraphEditingService
{
    void UpdateConnectorStates(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges);
}
