namespace GalNet.Core.Entry;

public sealed class PlayParticleEmitterEntry : PrimitiveEntry
{
    public const string TypeId = "particle.play";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("instanceId", EntryParameterType.Text), ("z", EntryParameterType.Float), ("parameters", EntryParameterType.Json));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(("z", "100"), ("parameters", "{}"));
}

public sealed class StopParticleEmitterEntry : PrimitiveEntry
{
    public const string TypeId = "particle.stop";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(("instanceId", EntryParameterType.Text));
}

public sealed class BurstParticlesEntry : PrimitiveEntry
{
    public const string TypeId = "particle.burst";
    public override string Type => TypeId;
    public static IReadOnlyDictionary<string, EntryParameterType> ParameterTypes { get; } = EntrySchema.Parameters(
        ("count", EntryParameterType.Integer), ("z", EntryParameterType.Float), ("parameters", EntryParameterType.Json));
    public static IReadOnlyDictionary<string, string> DefaultValues { get; } = EntrySchema.Defaults(
        ("count", "1"), ("z", "100"), ("parameters", "{}"));
}
