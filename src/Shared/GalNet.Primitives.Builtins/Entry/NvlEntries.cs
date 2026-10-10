using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

public sealed class ShowNvlEntry : PrimitiveEntry
{
    public const string TypeId = "nvl.show";
    public override string Type => TypeId;
}

public sealed class HideNvlEntry : PrimitiveEntry
{
    public const string TypeId = "nvl.hide";
    public override string Type => TypeId;
}

public sealed class ClearNvlEntry : PrimitiveEntry
{
    public const string TypeId = "nvl.clear";
    public override string Type => TypeId;
}

public sealed class AppendNvlEntry : PrimitiveEntry
{
    public const string TypeId = "nvl.append";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } =
        EntrySchema.Parameters(("content", EntryParameterType.MultilineText));
}
