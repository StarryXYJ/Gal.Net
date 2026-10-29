using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Presentation.Abstractions.Application;

namespace GalNet.Avalonia.GameView.ViewModels;

public partial class GamePageViewModel
{
    [ObservableProperty] private bool _isHistoryOpen;
    [ObservableProperty] private IReadOnlyList<DialogueHistoryLine> _historyLines = [];
    [ObservableProperty] private bool _canReplayDialogue;
    [ObservableProperty] private bool _isVoiceReplaying;
    [ObservableProperty] private IBrush? _readTextBrush;
    private DialogueHistoryLine? _currentDialogue;
    private Func<IReadOnlyList<DialogueHistoryLine>>? _getHistoryLines;
    public Func<IReadOnlyList<DialogueHistoryLine>>? GetHistoryLines
    {
        get => _getHistoryLines;
        set { _getHistoryLines = value; OnPropertyChanged(nameof(HasHistory)); }
    }
    public Func<DialogueVoice, Task>? PlayVoiceAsync { get; set; }
    public event Action? StopVoiceReplayRequested;
    public bool HasHistory => GetHistoryLines is not null;
    public bool IsHistoryEmpty => HistoryLines.Count == 0;
    public string ReplayIcon => IsVoiceReplaying ? "■" : "🔊";

    public void SetCurrentDialogue(DialogueHistoryLine line, bool voiceFinished)
    {
        _currentDialogue = line;
        CanReplayDialogue = line.Voice is not null && voiceFinished;
    }
    public void CompleteDialogueVoice(DialogueHistoryLine line)
    {
        if (ReferenceEquals(line, _currentDialogue)) CanReplayDialogue = line.Voice is not null;
    }

    public void NotifyStoryContinuing() => StopVoiceReplayRequested?.Invoke();

    [RelayCommand]
    private void OpenHistory()
    {
        if (!HasHistory || IsHistoryOpen || IsSkipConfirmationOpen) return;
        CancelPlayback();
        HistoryLines = GetHistoryLines!();
        IsHistoryOpen = true;
    }

    [RelayCommand]
    private void CloseHistory()
    {
        if (!IsHistoryOpen) return;
        IsHistoryOpen = false;
        StopVoiceReplayRequested?.Invoke();
    }

    [RelayCommand]
    private Task ReplayDialogueAsync() => CanReplayDialogue && _currentDialogue is { } line ? ReplayHistoryVoiceAsync(line) : Task.CompletedTask;

    [RelayCommand]
    private async Task ReplayHistoryVoiceAsync(DialogueHistoryLine line)
    {
        CancelPlayback();
        if (IsVoiceReplaying)
        {
            StopVoiceReplayRequested?.Invoke();
            // A repeated click on the current dialogue toggles playback off.
            if (ReferenceEquals(line, _replayingLine)) { _replayingLine = null; return; }
        }
        if (line.Voice is not { } voice || PlayVoiceAsync is null) return;
        _replayingLine = line;
        try { await PlayVoiceAsync(voice); }
        catch (Exception error) { StatusMessage = error.Message; }
    }
    private DialogueHistoryLine? _replayingLine;

    public void ResetDialogueHistory()
    {
        CloseHistory();
        StopVoiceReplayRequested?.Invoke();
        HistoryLines = [];
        _currentDialogue = null;
        _replayingLine = null;
        CanReplayDialogue = false;
        IsVoiceReplaying = false;
    }
    partial void OnIsVoiceReplayingChanged(bool value) => OnPropertyChanged(nameof(ReplayIcon));
    partial void OnHistoryLinesChanged(IReadOnlyList<DialogueHistoryLine> value) => OnPropertyChanged(nameof(IsHistoryEmpty));
}
