using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal sealed class TextCardPrimitiveInstance(ITextCardPresenter? presenter, PrimitiveCreateContext context, TextCardOptions options)
    : AsyncPrimitiveInstance(context.BatchId, options.Blocking, context.ScopeCancellation)
{
    private readonly CancellationTokenSource _skip = new();
    private readonly object _gate = new();
    private bool _finished;
    public override bool IsSkippable => options.Blocking;
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _skip.Token);
        try
        {
            if (presenter is not null)
                await presenter.ShowTextCardAsync(new TextCardRequest(context.Runtime.TextResolver.Resolve(options.Content),
                    options.Position, options.StyleName, options.HoldSeconds), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { lock (_gate) { _finished = true; _skip.Dispose(); } }
    }
    protected override void OnSkip() { lock (_gate) if (!_finished) _skip.Cancel(); }
    protected override void OnCatchUpDispatch() { lock (_gate) { _finished = true; _skip.Dispose(); } CompleteWithoutExecution(); }
    protected override void OnQuickDispatch() => OnCatchUpDispatch();
    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode) { if (mode != PrimitiveInvocationMode.Normal) OnSkip(); }
}
