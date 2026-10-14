using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

public sealed class PlayParticleEmitterEntry : PrimitiveEntry
{
    public const string TypeId = "particle.play";
    public override string Type => TypeId;
}

public sealed class StopParticleEmitterEntry : PrimitiveEntry
{
    public const string TypeId = "particle.stop";
    public override string Type => TypeId;
}

public sealed class BurstParticlesEntry : PrimitiveEntry
{
    public const string TypeId = "particle.burst";
    public override string Type => TypeId;
}
