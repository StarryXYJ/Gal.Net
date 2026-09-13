using System.Diagnostics;

namespace GalNet.Rendering.Scene;

/// <summary>Hard limits for optional texture-to-texture work. Base scene rendering is never rejected.</summary>
public sealed record SceneRenderBudget(
    int MaxEffectPasses = 32,
    int MaxIntermediatePixels = 16_777_216,
    long MaxIntermediateTextureBytes = 67_108_864)
{
    public static SceneRenderBudget Default { get; } = new();
}

public sealed record SceneRenderDiagnosticEvent(DateTimeOffset Timestamp, string Code, string Message);

public sealed record SceneRenderDiagnosticsSnapshot(
    long FrameCount,
    TimeSpan TotalRenderTime,
    TimeSpan LastRenderTime,
    int LastEffectPassCount,
    long LastPeakIntermediateTextureBytes,
    bool LastFrameUsedGpu,
    IReadOnlyList<SceneRenderDiagnosticEvent> Events);

/// <summary>Thread-safe, bounded renderer telemetry suitable for a future debug overlay or log sink.</summary>
public sealed class SceneRenderDiagnostics
{
    private const int MaxEvents = 64;
    private readonly object _gate = new();
    private readonly Queue<SceneRenderDiagnosticEvent> _events = [];
    private long _frameCount;
    private TimeSpan _totalRenderTime;
    private TimeSpan _lastRenderTime;
    private int _lastEffectPassCount;
    private long _lastPeakIntermediateTextureBytes;
    private bool _lastFrameUsedGpu;

    public SceneRenderDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new SceneRenderDiagnosticsSnapshot(
                _frameCount,
                _totalRenderTime,
                _lastRenderTime,
                _lastEffectPassCount,
                _lastPeakIntermediateTextureBytes,
                _lastFrameUsedGpu,
                _events.ToArray());
        }
    }

    internal void CompleteFrame(TimeSpan elapsed, int effectPassCount, long peakIntermediateTextureBytes, bool usedGpu)
    {
        lock (_gate)
        {
            _frameCount++;
            _totalRenderTime += elapsed;
            _lastRenderTime = elapsed;
            _lastEffectPassCount = effectPassCount;
            _lastPeakIntermediateTextureBytes = peakIntermediateTextureBytes;
            _lastFrameUsedGpu = usedGpu;
        }
    }

    internal void Report(string code, string message)
    {
        lock (_gate)
        {
            _events.Enqueue(new SceneRenderDiagnosticEvent(DateTimeOffset.UtcNow, code, message));
            while (_events.Count > MaxEvents) _events.Dequeue();
        }
    }
}

/// <summary>Options shared by one renderer invocation. Omit it to use the default safety budget.</summary>
public sealed record SceneRenderOptions(SceneRenderBudget Budget, SceneRenderDiagnostics? Diagnostics = null)
{
    public static SceneRenderOptions Default { get; } = new(SceneRenderBudget.Default);
}

internal sealed class SceneRenderBudgetGuard(SceneRenderOptions? options, bool isGpu) : IDisposable
{
    private readonly SceneRenderOptions _options = options ?? SceneRenderOptions.Default;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _effectPassCount;
    private long _peakIntermediateTextureBytes;

    public bool TryAcquireEffectPass(SceneEffectInstance effect, int width, int height)
    {
        var pixels = (long)width * height;
        var bytes = pixels > long.MaxValue / 4 ? long.MaxValue : pixels * 4L;
        if (pixels > _options.Budget.MaxIntermediatePixels)
        {
            Report("effect.skipped.pixel-budget", $"Effect '{effect.InstanceId}' was skipped because {pixels} intermediate pixels exceed the budget of {_options.Budget.MaxIntermediatePixels}.");
            return false;
        }
        if (bytes > _options.Budget.MaxIntermediateTextureBytes)
        {
            Report("effect.skipped.texture-budget", $"Effect '{effect.InstanceId}' was skipped because its {bytes} byte intermediate texture exceeds the budget of {_options.Budget.MaxIntermediateTextureBytes} bytes.");
            return false;
        }
        if (_effectPassCount >= _options.Budget.MaxEffectPasses)
        {
            Report("effect.skipped.pass-budget", $"Effect '{effect.InstanceId}' was skipped because the frame reached the {_options.Budget.MaxEffectPasses} effect-pass budget.");
            return false;
        }

        _effectPassCount++;
        _peakIntermediateTextureBytes = Math.Max(_peakIntermediateTextureBytes, bytes);
        return true;
    }

    public void ReportEffectFailure(SceneEffectInstance effect, Exception error) =>
        Report("effect.identity-on-failure", $"Effect '{effect.InstanceId}' failed and its input was preserved: {error.Message}");

    public void Report(string code, string message) => _options.Diagnostics?.Report(code, message);

    public void Dispose()
    {
        _clock.Stop();
        _options.Diagnostics?.CompleteFrame(_clock.Elapsed, _effectPassCount, _peakIntermediateTextureBytes, isGpu);
    }
}
