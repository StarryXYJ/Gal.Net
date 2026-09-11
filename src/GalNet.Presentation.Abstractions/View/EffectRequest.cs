namespace GalNet.Core.View;

/// <summary>
/// A host-defined effect request. Parameters are intentionally opaque to the runtime.
/// </summary>
public sealed record EffectRequest(
    string Id,
    string InstanceId,
    string TargetHandleId = "",
    string Parameters = "");
