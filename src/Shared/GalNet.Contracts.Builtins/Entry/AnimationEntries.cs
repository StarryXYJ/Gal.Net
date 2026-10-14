using GalNet.Core.Entry;
using GalNet.Core.Scene;

namespace GalNet.Contracts.Builtins;

/// <summary>Plays one interpolated property animation on an active scene instance.</summary>
public sealed class AnimateEntry : PrimitiveEntry
{
    public const string TypeId = "animation.animate";
    public override string Type => TypeId;
}

public sealed class PlayAnimationPlanEntry : PrimitiveEntry
{
    public const string TypeId = "animation.play";
    public override string Type => TypeId;
}

public sealed class StopAnimationEntry : PrimitiveEntry
{
    public const string TypeId = "animation.stop";
    public override string Type => TypeId;
}
