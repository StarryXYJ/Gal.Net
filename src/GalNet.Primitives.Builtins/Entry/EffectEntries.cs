using GalNet.Core.Entry;

namespace GalNet.Primitives.Builtins;

public sealed class ApplyEffectEntry : PrimitiveEntry
{
    public const string TypeId = "effect.apply";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("program", EntryParameterType.EffectProgramAsset), ("id", EntryParameterType.Text), ("instanceId", EntryParameterType.Text), ("targetHandleId", EntryParameterType.Text), ("order", EntryParameterType.Integer), ("parameters", EntryParameterType.Json));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("program", ""), ("id", ""), ("targetHandleId", ""), ("order", "0"), ("parameters", "{}"));
}

public sealed class StopEffectEntry : PrimitiveEntry { public const string TypeId = "effect.stop"; public override string Type => TypeId; public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("instanceId", EntryParameterType.Text)); }
