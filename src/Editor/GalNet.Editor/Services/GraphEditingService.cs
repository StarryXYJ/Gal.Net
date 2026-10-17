using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public sealed class GraphEditingService : IGraphEditingService
{
    private readonly IEntryCatalog _catalog;
    public GraphEditingService(IEntryCatalog catalog) => _catalog = catalog;

    public IReadOnlyList<EntryEditorItemViewModel> InsertEntries(GraphNode groupNode, int index, int count, string? type = null)
    {
        if (groupNode.NodeKind != GraphNodeKind.LinearGroup || count < 1)
            return [];

        var definition = _catalog.Definitions.FirstOrDefault(item => item.Type == (type ?? GalNet.Contracts.Builtins.BuiltinEntryIds.Text));
        if (definition is null)
            return [];

        index = Math.Clamp(index, 0, groupNode.Entries.Count);
        var inserted = new List<EntryEditorItemViewModel>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var entry = new EntryEditorItemViewModel(_catalog)
            {
                StableId = Guid.NewGuid().ToString("N"),
                Type = definition.Type,
                Parameters = new Dictionary<string, string>(
                    _catalog.Create(definition.Type).Values,
                    StringComparer.Ordinal)
            };
            groupNode.Entries.Insert(index + offset, entry);
            inserted.Add(entry);
        }
        RenumberEntries(groupNode);
        return inserted;
    }

    public bool RemoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry)
    {
        var removed = groupNode.Entries.Remove(entry);
        if (removed)
            RenumberEntries(groupNode);

        return removed;
    }

    public bool MoveEntry(GraphNode groupNode, EntryEditorItemViewModel entry, int newIndex)
    {
        var oldIndex = groupNode.Entries.IndexOf(entry);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= groupNode.Entries.Count || oldIndex == newIndex)
            return false;

        groupNode.Entries.Move(oldIndex, newIndex);
        RenumberEntries(groupNode);
        return true;
    }

    public void UpdateConnectorStates(ObservableCollection<GraphNode> nodes, ObservableCollection<GraphEdge> edges)
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

    private static void RenumberEntries(GraphNode groupNode)
    {
        for (var index = 0; index < groupNode.Entries.Count; index++)
            groupNode.Entries[index].Id = index + 1;
    }

}
