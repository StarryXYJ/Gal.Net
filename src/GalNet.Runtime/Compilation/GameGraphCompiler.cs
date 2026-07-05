using GalNet.Core.Entry;
using GalNet.Core.Graph;

namespace GalNet.Runtime.Compilation;

public sealed class GameGraphCompiler : IGameGraphCompiler
{
    public static GameGraphCompiler Default { get; } = new();

    public IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> Compile(Graph graph)
    {
        var result = new Dictionary<string, IReadOnlyList<SimpleEntry>>();

        foreach (var node in graph.Nodes.OfType<Group>())
        {
            var compiled = new List<SimpleEntry>();
            foreach (var complex in node.Entries)
            {
                compiled.AddRange(complex.Compile());
            }

            result[node.Id] = compiled;
        }

        return result;
    }
}
