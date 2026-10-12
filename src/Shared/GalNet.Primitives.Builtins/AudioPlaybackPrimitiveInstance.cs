using GalNet.Core.Primitives;
using GalNet.Runtime.Audio;

namespace GalNet.Primitives.Builtins;

/// <summary>A non-flow-blocking command whose finite logical playback can block automatic advance.</summary>
public sealed class AudioPlaybackPrimitiveInstance(
    Func<AudioPlaybackTicket> play,
    bool autoBlocking,
    string? batchId,
    CancellationToken cancellationToken) : PrimitiveInstance(batchId)
{
    private AudioPlaybackTicket? _ticket;
    public override bool IsBlocking => false;
    public override bool IsSkippable => autoBlocking;

    protected override void OnDispatch()
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ticket = play();
        if (!autoBlocking) { TryComplete(); return; }
        SetAutoBlocking(true);
        _ = ObserveAsync(_ticket);
    }

    protected override void OnQuickDispatch()
    {
        if (autoBlocking) TryComplete();
        else OnDispatch();
    }

    protected override void OnCatchUpDispatch()
    {
        if (autoBlocking) TryComplete();
        else OnDispatch();
    }

    protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode)
    {
        if (autoBlocking && mode != PrimitiveInvocationMode.Normal) OnSkip();
    }

    protected override void OnSkip() { _ticket?.Stop(); TryComplete(); }

    private async Task ObserveAsync(AudioPlaybackTicket ticket)
    {
        try { await ticket.Completion.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { ticket.Stop(); }
        finally { TryComplete(); }
    }
}
