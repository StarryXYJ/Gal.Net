using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Sample.CrossPlatform.Opening;

public enum OpeningShowPolicy { EveryLaunch, OncePerProfile, Disabled }

/// <summary>One presentation instance. All asynchronous work must observe its lifetime token.</summary>
public abstract class OpeningPageViewModel : PageViewModelBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<OpeningPageViewModel>? _finish;
    private Task? _start;
    private Task? _disposal;

    public abstract string Id { get; }
    public virtual bool CanSkip => true;
    public virtual OpeningShowPolicy ShowPolicy => OpeningShowPolicy.EveryLaunch;
    internal Task Finished => _finished.Task;

    public abstract Task StartAsync(CancellationToken cancellationToken);
    protected virtual ValueTask StopAsync() => ValueTask.CompletedTask;

    /// <summary>Only this page's active presentation can finish; late callbacks are harmless.</summary>
    protected void Finish() => _finish?.Invoke(this);

    internal void Start(Action<OpeningPageViewModel> finish)
    {
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        if (_start is not null) throw new InvalidOperationException("An opening page cannot be reused.");
        _finish = finish;
        _start = ObserveStartAsync();
    }

    internal void Complete() => _finished.TrySetResult();

    private async Task ObserveStartAsync()
    {
        try { await StartAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { _finished.TrySetException(error); throw; }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _finish = null;
        var cleanup = new GalNet.GameHost.ResourceCleanup();
        cleanup.Run(_lifetime.Cancel);
        if (_start is not null) await cleanup.RunAsync(() => new ValueTask(_start));
        await cleanup.RunAsync(StopAsync);
        cleanup.Run(_lifetime.Dispose);
        cleanup.ThrowIfFailed();
    }
}

/// <summary>The view supplies the visual animation; the page owns its cancellation and completion.</summary>
public abstract class FadingOpeningPageViewModel : OpeningPageViewModel
{
    public virtual TimeSpan FadeIn => TimeSpan.FromSeconds(0.5);
    public virtual TimeSpan Hold => TimeSpan.FromSeconds(0.8);
    public virtual TimeSpan FadeOut => TimeSpan.FromSeconds(0.6);
    internal Func<CancellationToken, Task>? AnimateAsync { get; set; }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var animate = AnimateAsync ?? throw new InvalidOperationException("Mount the opening view before starting its page.");
        await animate(cancellationToken);
        Finish();
    }
}

public sealed class EngineLogoPageViewModel : FadingOpeningPageViewModel
{
    public override string Id => "galnet-logo";
}

public sealed class DisclaimerPageViewModel(GalNet.Avalonia.GameHost.IPlayerLocalization localization) : FadingOpeningPageViewModel
{
    public override string Id => "play-notice";
    public DynamicLocalization.Core.LocalizedString Title { get; } = localization.GetString("Opening.NoticeTitle");
    public DynamicLocalization.Core.LocalizedString FictionNotice { get; } = localization.GetString("Opening.FictionNotice");
    public DynamicLocalization.Core.LocalizedString FlashingNotice { get; } = localization.GetString("Opening.FlashingNotice");
}
