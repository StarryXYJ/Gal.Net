using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class AvaloniaDialoguePresenter : IDialoguePresenter, INvlPresenter, IChoicePresenter, ICatchUpTextRecorder, IDisposable
{
    private readonly TaskCompletionSource _initialPresentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly GamePageViewModel _state;
    private readonly GamePage _page;
    private readonly IAvaloniaUiDispatcher _dispatcher;
    private readonly List<string> _nvlHistory = [];
    private bool _catchUp;
    private bool _dialogueVisible;
    private bool _nvlVisible;
    private string? _text;
    private string _speaker = "";

    public AvaloniaDialoguePresenter(GamePageViewModel state, GamePage page, IAvaloniaUiDispatcher dispatcher)
    {
        _state = state;
        _page = page;
        _dispatcher = dispatcher;
        _state.NvlClearRequested += OnNvlClearRequested;
    }

    public Task InitialPresentationReady => _initialPresentationReady.Task;

    public void CompleteInitialPresentation() => _initialPresentationReady.TrySetResult();
    public void FailInitialPresentation(Exception exception) => _initialPresentationReady.TrySetException(exception);
    public void ShowDialogue() => _dispatcher.Dispatch(() => { _dialogueVisible = true; if (!_catchUp) _state.IsDialogueVisible = true; });
    public void HideDialogue() => _dispatcher.Dispatch(() => { _dialogueVisible = false; if (!_catchUp) _state.IsDialogueVisible = false; });

    public void ShowNvl() => _dispatcher.Dispatch(() => { _nvlVisible = true; if (!_catchUp) _state.IsNvlMode = true; });
    public void HideNvl() => _dispatcher.Dispatch(() => { _nvlVisible = false; if (!_catchUp) _state.IsNvlMode = false; });
    public void ClearNvl() => _dispatcher.Dispatch(() => { _nvlHistory.Clear(); if (!_catchUp) _page.Nvl.Clear(); });
    public Task AppendNvlTextAsync(string text, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            _page.EnsureLayout();
            _nvlHistory.Add(text);
            return _page.Nvl.AppendAsync(text, _state.TextSpeed, cancellationToken);
        });
    public void SkipNvlText() => _dispatcher.Dispatch(_page.Nvl.SkipText);
    public void RevealAllNvlText() => _dispatcher.Dispatch(_page.Nvl.RevealAllText);
    public Task AppendNvlTextImmediatelyAsync(string text, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            _page.EnsureLayout();
            _nvlHistory.Add(text);
            var completion = _page.Nvl.AppendAsync(text, 0, cancellationToken);
            _page.Nvl.RevealAllText();
            return completion;
        });

    public Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(async () =>
        {
            _page.EnsureLayout();
            _speaker = speaker;
            _text = text;
            _dialogueVisible = true;
            _state.IsDialogueVisible = true;
            _page.Dialogue.Speaker = speaker;
            _page.Dialogue.Text = text;
            _page.Dialogue.CharactersPerSecond = _state.TextSpeed;
            await _page.Dialogue.StartAsync(cancellationToken);
        });

    public void SkipTypewriter(string widgetInstanceId) => _dispatcher.Dispatch(_page.Dialogue.Skip);
    public Task WaitForClickAsync(CancellationToken cancellationToken) => _dispatcher.InvokeAsync(() =>
    {
        var wait = _state.WaitForAdvanceAsync(cancellationToken);
        _initialPresentationReady.TrySetResult();
        return wait;
    });

    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            _page.EnsureLayout();
            var wait = _state.WaitForChoiceAsync(options, cancellationToken);
            _initialPresentationReady.TrySetResult();
            return wait;
        });

    Task IDialoguePresenter.PresentTextAsync(string speaker, string text, CancellationToken cancellationToken) =>
        StartTypewriter("default_dialogue", speaker, text, cancellationToken);

    void IDialoguePresenter.SkipText() => SkipTypewriter("default_dialogue");
    public void RevealAllText() => _dispatcher.Dispatch(_page.Dialogue.RevealAll);
    public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            _page.EnsureLayout();
            _speaker = speaker;
            _text = text;
            _dialogueVisible = true;
            _state.IsDialogueVisible = true;
            _page.Dialogue.Speaker = speaker;
            _page.Dialogue.Text = text;
            _page.Dialogue.CharactersPerSecond = 0;
            var completion = _page.Dialogue.StartAsync(cancellationToken);
            _page.Dialogue.RevealAll();
            return completion;
        });

    Task<int> IChoicePresenter.ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken) =>
        WaitForChoiceAsync("default_choice", options.ToArray(), cancellationToken);

    public void Dispose()
    {
        _state.NvlClearRequested -= OnNvlClearRequested;
    }

    public void RecordDialogue(string speaker, string text) => _dispatcher.Dispatch(() =>
    {
        _speaker = speaker;
        _text = text;
        _dialogueVisible = true;
    });

    public void RecordNvlParagraph(string text) => _dispatcher.Dispatch(() => _nvlHistory.Add(text));

    public Task BeginCatchUpAsync() => _dispatcher.InvokeAsync(() =>
    {
        _dialogueVisible = _state.IsDialogueVisible;
        _nvlVisible = _state.IsNvlMode;
        _catchUp = true;
        return Task.CompletedTask;
    });

    public Task EndCatchUpAsync(CancellationToken cancellationToken) => _dispatcher.InvokeAsync(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        _page.EnsureLayout();
        _page.Nvl.Clear();
        foreach (var paragraph in _nvlHistory)
        {
            var completion = _page.Nvl.AppendAsync(paragraph, 0, cancellationToken);
            _page.Nvl.RevealAllText();
            await completion;
        }
        if (_text is not null)
        {
            _page.Dialogue.Speaker = _speaker;
            _page.Dialogue.Text = _text;
            _page.Dialogue.CharactersPerSecond = 0;
            var completion = _page.Dialogue.StartAsync(cancellationToken);
            _page.Dialogue.RevealAll();
            await completion;
        }
        _state.IsDialogueVisible = _dialogueVisible;
        _state.IsNvlMode = _nvlVisible;
        _catchUp = false;
    });

    private void OnNvlClearRequested() => ClearNvl();
}
