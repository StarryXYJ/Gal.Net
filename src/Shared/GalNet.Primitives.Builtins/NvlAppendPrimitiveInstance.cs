using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

/// <summary>NVL operations for the shared text and advance phases.</summary>
public sealed class NvlAppendPrimitiveInstance : TextAdvancePrimitiveInstance
{
    private readonly INvlPresenter _presenter;
    private readonly string _text;
    public NvlAppendPrimitiveInstance(INvlPresenter presenter, string text,
        string? batchId, CancellationToken cancellationToken) : base(batchId, cancellationToken)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _text = text;
    }
    protected override Task PresentTextAsync(bool immediately, CancellationToken token) => immediately
        ? _presenter.AppendNvlTextImmediatelyAsync(_text, token)
        : _presenter.AppendNvlTextAsync(_text, token);
    protected override void SkipText() => _presenter.SkipNvlText();
    protected override void RevealAllText() => _presenter.RevealAllNvlText();
    protected override void RecordCatchUpText()
    {
        if (_presenter is ICatchUpTextRecorder recorder) recorder.RecordNvlParagraph(_text);
    }
    protected override void HandlePresentationFailure(Exception exception) =>
        GameLog.Logger.Warning(exception, "NVL presentation failed");
}
