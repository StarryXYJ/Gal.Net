namespace GalNet.Core.View;

/// <summary>
/// A host-defined transition request. The runtime only owns its lifecycle;
/// the host resolves <see cref="Id"/> and parses <see cref="Parameters"/>.
/// </summary>
public sealed record TransitionRequest(
    string Id,
    string? FromImageId,
    string? ToImageId,
    TimeSpan Duration,
    bool IsBlocking,
    string Parameters = "");
