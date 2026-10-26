namespace GalNet.Core.Execution;

/// <summary>Immutable scheduling input supplied by the host, independent of its preference model.</summary>
public sealed record PlaybackOptions
{
    public int AutoAdvanceIntervalMilliseconds { get; init; } = 2000;
    public int QuickAdvanceIntervalMilliseconds { get; init; } = 50;
    public bool QuickReadOnly { get; init; } = true;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(AutoAdvanceIntervalMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(QuickAdvanceIntervalMilliseconds);
    }
}
