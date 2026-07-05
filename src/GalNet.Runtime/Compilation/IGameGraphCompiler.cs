using GalNet.Core.Entry;
using GalNet.Core.Graph;

namespace GalNet.Runtime.Compilation;

public interface IGameGraphCompiler
{
    IReadOnlyDictionary<string, IReadOnlyList<SimpleEntry>> Compile(Graph graph);
}
