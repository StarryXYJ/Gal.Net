namespace GalNet.Editor.Abstraction.Services;

public sealed record ProjectSessionOptions
{
    public TimeSpan RecoveryInterval { get; init; } = TimeSpan.FromSeconds(30);
    public int RecoveryCopies { get; init; } = 3;
    public int HistorySteps { get; init; } = 200;
    public long RetainedByteBudget { get; init; } = 1024L * 1024 * 1024;
}
