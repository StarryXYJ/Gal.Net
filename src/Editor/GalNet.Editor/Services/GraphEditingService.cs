using System.Collections.Generic;
using System.Linq;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed class GraphEditingService : IGraphEditingService
{
    public void UpdateConnectorStates(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge> edges)
    {
        foreach (var connector in nodes.SelectMany(node => node.InputConnectors.Concat(node.OutputConnectors)))
            connector.IsConnected = false;

        foreach (var edge in edges)
        {
            var output = edge.From.OutputConnectors.FirstOrDefault(connector => connector.Index == edge.Outlet);
            if (output is not null)
                output.IsConnected = true;

            var input = edge.To.InputConnectors.FirstOrDefault();
            if (input is not null)
                input.IsConnected = true;
        }
    }
}
