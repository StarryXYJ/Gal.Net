namespace GalNet.Presentation.Abstractions.View;

/// <summary>
/// A host-defined effect request. Parameters are intentionally opaque to the runtime.
/// </summary>
public sealed record EffectRequest(
    string Id,
    string InstanceId,
    string TargetHandleId = "",
    int Order = 0,
    string Parameters = "",
    string ProgramResource = "")
{
    /// <summary>Stable animation values to apply after an active effect is recreated.</summary>
    public IReadOnlyDictionary<string, float> AnimationValues { get; init; } = new Dictionary<string, float>(StringComparer.Ordinal);
}
