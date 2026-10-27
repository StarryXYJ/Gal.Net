using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.Core.Assets;
using GalNet.Core.Parameters;

[assembly: FieldSchemaJsonContext(typeof(GalNet.Sample.Headless.Acceptance.SmokeJsonContext))]

namespace GalNet.Sample.Headless.Acceptance;

public sealed class SmokeState
{
    public string Id { get; set; } = "";
    public int Count { get; set; }
}

internal sealed class MissingMetadata;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SmokeState[]))]
[JsonSerializable(typeof(SmokeOptions))]
[JsonSerializable(typeof(SmokeSettings))]
[JsonSerializable(typeof(SmokeMeta))]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;

[GenerateFieldSchema]
public partial class SmokeOptions
{
    [Field(0)] public int Count { get; set; } = 2;
    [Field(1)] public SmokeChild? Child { get; set; }
    [Field(2), JsonConverter(typeof(StringBooleanValueConverter))] public bool? Enabled { get; set; } = true;
    [Field(3), JsonConverter(typeof(StringEnumValueConverter<SmokeMode>))] public SmokeMode? Mode { get; set; } = SmokeMode.First;
}

public enum SmokeMode { First, Second }

[GenerateFieldSchema]
public partial class SmokeChild
{
    [Field(0)] public string Text { get; set; } = "";
    [Field(1)] public SmokeChild? Next { get; set; }
}

[GenerateFieldSchema]
public partial class SmokeSettings
{
    [Field(0, Minimum = 0)] public int Count { get; set; } = 3;
}

public sealed class SmokeMeta : AssetMeta
{
    public string Label { get; set; } = "";
}

internal sealed class SmokeDispatcher(StateCatalog catalog) : IPrimitiveDispatcher
{
    public StateCatalog StateCatalog => catalog;
    public IReadOnlyCollection<EntryDefinition> Primitives => [];
    public bool CanExecute(string primitiveType) => primitiveType == "project.increment";
    public bool TryGetEntry(string primitiveType, out EntryDefinition? entry) { entry = null; return false; }
    public PrimitiveInstance Dispatch(PrimitiveEntry entry, IGameContext runtime, CancellationToken cancellationToken,
        PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal)
    {
        var instance = new ImmediatePrimitiveInstance(() =>
        {
            runtime.State.Update(() => runtime.State.Table<SmokeState>().Get("counter").Count++);
            runtime.SetVariable("save.count", runtime.GetVariable("save.count")!.AsInt() + 1);
        }, batchId: entry.BatchId);
        instance.Dispatch(mode);
        return instance;
    }
}
