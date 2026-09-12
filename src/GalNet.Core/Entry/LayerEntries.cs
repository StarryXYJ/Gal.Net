using GalNet.Core.Scene;

namespace GalNet.Core.Entry;

public sealed class ShowLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.show";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("handleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset), ("flipbook", EntryParameterType.Json), ("transform", EntryParameterType.Json), ("z", EntryParameterType.Float), ("opacity", EntryParameterType.Float), ("displayMode", EntryParameterType.Select));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("opacity", "1"), ("displayMode", "Native"));
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParameterOptions { get; } = EntrySchema.Options(("displayMode", ["Native", "Tile", "Fill", "Uniform", "UniformToFill"]));
}

/// <summary>Shows a transient solid-color layer, primarily for compiled color-field transitions.</summary>
public sealed class ShowColorLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.showColor";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("handleId", EntryParameterType.Text), ("color", EntryParameterType.Text), ("transform", EntryParameterType.Json), ("z", EntryParameterType.Float), ("opacity", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "1000"), ("opacity", "1"));
}

public sealed class HideLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.hide";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("handleId", EntryParameterType.Text));
}

public sealed class MoveLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.move";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("handleId", EntryParameterType.Text), ("transform", EntryParameterType.Json), ("z", EntryParameterType.Float), ("duration", EntryParameterType.Float));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("transform", "{}"), ("z", "0"), ("duration", "0.5"));
}

public sealed class ReplaceLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.replace";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("handleId", EntryParameterType.Text), ("assetId", EntryParameterType.ImageAsset));
}
