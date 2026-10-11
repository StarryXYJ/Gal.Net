using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

/// <summary>Owns the typewriter and advance phases for one appended NVL line.</summary>
public sealed class NvlAppendPrimitiveInstance : PrimitiveInstance
{
    private readonly object _gate = new();
    private readonly INvlPresenter _presenter;
    private readonly string _text;
    private readonly CancellationToken _cancellationToken;
    private NvlAppendPhase _phase;

    public NvlAppendPrimitiveInstance(
        INvlPresenter presenter,
        string text,
        string? batchId,
        CancellationToken cancellationToken)
        : base(batchId)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _text = text;
        _cancellationToken = cancellationToken;
    }

    public override bool IsBlocking => true;

    public override bool IsSkippable
    {
        get
        {
            lock (_gate)
                return _phase is NvlAppendPhase.Typing or NvlAppendPhase.WaitingAdvance;
        }
    }

    protected override void OnDispatch()
    {
        lock (_gate)
        {
            if (_phase != NvlAppendPhase.Created)
                throw new InvalidOperationException("NVL append has already started.");
            _phase = NvlAppendPhase.Typing;
        }

        _ = ObservePresentationAsync();
    }

    protected override void OnSkip()
    {
        var complete = false;
        lock (_gate)
        {
            switch (_phase)
            {
                case NvlAppendPhase.Typing:
                    _presenter.SkipNvlText();
                    break;
                case NvlAppendPhase.WaitingAdvance:
                    _phase = NvlAppendPhase.Completed;
                    complete = true;
                    break;
            }
        }
        if (complete)
            TryComplete();
    }

    private async Task ObservePresentationAsync()
    {
        try
        {
            await _presenter.AppendNvlTextAsync(_text, _cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_phase == NvlAppendPhase.Typing)
                    _phase = NvlAppendPhase.WaitingAdvance;
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            CompleteAfterPresentationFailure();
        }
        catch (Exception error)
        {
            GameLog.Logger.Warning(error, "NVL presentation failed");
            CompleteAfterPresentationFailure();
        }
    }

    private void CompleteAfterPresentationFailure()
    {
        lock (_gate)
            _phase = NvlAppendPhase.Completed;
        TryComplete();
    }

    private enum NvlAppendPhase
    {
        Created,
        Typing,
        WaitingAdvance,
        Completed
    }
}
