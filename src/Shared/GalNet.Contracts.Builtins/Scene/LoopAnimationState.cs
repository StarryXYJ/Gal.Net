namespace GalNet.Contracts.Builtins.Scene;

/// <summary>Typed replay definition, independent from live stop controls and clocks.</summary>
public sealed class LoopAnimationState
{
    public string Id { get; init; } = "";
    public AnimationRequest? Request { get; init; }
    public TimelineDefinition? Timeline { get; init; }
}
