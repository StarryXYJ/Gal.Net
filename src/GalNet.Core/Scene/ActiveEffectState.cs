namespace GalNet.Core.Scene;

/// <summary>Persisted descriptor for an effect that remains active until its instance handle is stopped.</summary>
public sealed class ActiveEffectState
{
    public string Id { get; init; } = "";
    public string InstanceId { get; init; } = "";
    /// <summary>Empty for an overlay effect; otherwise the layer handle it affects.</summary>
    public string TargetHandleId { get; init; } = "";
    public string Parameters { get; init; } = "{}";
}
