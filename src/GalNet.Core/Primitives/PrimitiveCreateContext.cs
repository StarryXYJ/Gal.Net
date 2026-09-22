using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Runtime;

namespace GalNet.Core.Primitives;

/// <summary>All immutable inputs used to create one primitive runtime instance.</summary>
public sealed class PrimitiveCreateContext
{
    public PrimitiveCreateContext(
        PrimitiveEntryBase definition,
        PrimitiveEntry entry,
        IGameRuntime runtime,
        CancellationToken scopeCancellation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(runtime);
        Definition = definition;
        Entry = entry;
        Runtime = runtime;
        ScopeCancellation = scopeCancellation;
    }

    public PrimitiveEntryBase Definition { get; }
    public PrimitiveEntry Entry { get; }
    public IGameRuntime Runtime { get; }
    public CancellationToken ScopeCancellation { get; }
    public DynamicParameterTable Parameters => Definition.Parameters;
    public JsonElement Arguments => Entry.Arguments;
    public string? BatchId => Entry.BatchId;
}
