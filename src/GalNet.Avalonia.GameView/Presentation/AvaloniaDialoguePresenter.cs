using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameView.Presentation;

public sealed class AvaloniaDialoguePresenter : IDialoguePresenter, IChoicePresenter, IDisposable
{
    private readonly TaskCompletionSource _initialPresentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly GamePageViewModel _state;
    private readonly GamePage _page;
    private readonly IAvaloniaUiDispatcher _dispatcher;

    public AvaloniaDialoguePresenter(GamePageViewModel state, GamePage page, IAvaloniaUiDispatcher dispatcher)
    {
        _state = state;
        _page = page;
        _dispatcher = dispatcher;
        _state.AdvanceRequested += OnAdvanceRequested;
    }

    public Task InitialPresentationReady => _initialPresentationReady.Task;
    public event Action? AdvanceRequested;

    public void CompleteInitialPresentation() => _initialPresentationReady.TrySetResult();
    public void FailInitialPresentation(Exception exception) => _initialPresentationReady.TrySetException(exception);
    public void ShowDialogue() => _dispatcher.Dispatch(() => _state.IsDialogueVisible = true);
    public void HideDialogue() => _dispatcher.Dispatch(() => _state.IsDialogueVisible = false);

    public Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(async () =>
        {
            if (_state.IsNvlMode)
            {
                _state.PresentNvlLine(speaker, text);
                return;
            }

            _state.IsDialogueVisible = true;
            _page.Dialogue.Speaker = speaker;
            _page.Dialogue.Text = text;
            _page.Dialogue.CharactersPerSecond = _state.TextSpeed;
            await _page.Dialogue.StartAsync(cancellationToken);
        });

    public void SkipTypewriter(string widgetInstanceId) => _dispatcher.Dispatch(_page.Dialogue.Skip);
    public void SetVoice(string assetId) =>
        _dispatcher.Dispatch(() => _state.StatusMessage = $"Voice requested: {assetId}");

    public Task WaitForClickAsync(CancellationToken cancellationToken) => _dispatcher.InvokeAsync(() =>
    {
        var wait = _state.WaitForAdvanceAsync(cancellationToken);
        _initialPresentationReady.TrySetResult();
        return wait;
    });

    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() =>
        {
            var wait = _state.WaitForChoiceAsync(options, cancellationToken);
            _initialPresentationReady.TrySetResult();
            return wait;
        });

    Task IDialoguePresenter.PresentTextAsync(string speaker, string text, CancellationToken cancellationToken) =>
        StartTypewriter("default_dialogue", speaker, text, cancellationToken);

    void IDialoguePresenter.SkipText() => SkipTypewriter("default_dialogue");

    Task<int> IChoicePresenter.ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken) =>
        WaitForChoiceAsync("default_choice", options.ToArray(), cancellationToken);

    public void Dispose() => _state.AdvanceRequested -= OnAdvanceRequested;

    private void OnAdvanceRequested() => AdvanceRequested?.Invoke();
}
