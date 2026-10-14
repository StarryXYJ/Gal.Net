using GalNet.Core.Entry;
using GalNet.Core.Scene;

namespace GalNet.Contracts.Builtins;

public sealed class ShowLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.show";
    public override string Type => TypeId;
}

/// <summary>Shows a transient solid-color layer, primarily for compiled color-field transitions.</summary>
public sealed class ShowColorLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.showColor";
    public override string Type => TypeId;
}

public sealed class HideLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.hide";
    public override string Type => TypeId;
}

public sealed class MoveLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.move";
    public override string Type => TypeId;
}

public sealed class ReplaceLayerEntry : PrimitiveEntry
{
    public const string TypeId = "layer.replace";
    public override string Type => TypeId;
}
