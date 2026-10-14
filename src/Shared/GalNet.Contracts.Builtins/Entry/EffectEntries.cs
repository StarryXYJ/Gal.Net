using GalNet.Core.Entry;

namespace GalNet.Contracts.Builtins;

public sealed class ApplyEffectEntry : PrimitiveEntry
{
    public const string TypeId = "effect.apply";
    public override string Type => TypeId;
}

public sealed class StopEffectEntry : PrimitiveEntry { public const string TypeId = "effect.stop"; public override string Type => TypeId; }
