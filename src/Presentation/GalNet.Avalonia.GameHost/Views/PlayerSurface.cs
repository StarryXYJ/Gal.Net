using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameHost.ViewModels;

namespace GalNet.Avalonia.GameHost.Views;

/// <summary>Reusable modal, focus and announcement behavior. The application supplies the named surface controls.</summary>
public abstract class PlayerSurface : UserControl
{
    private GameShell? _shell;
    private GamePresentationState? _audioGame;
    private bool _returnToTitle;
    private LanguageSelectionViewModel? _languageSelection;
    private TaskCompletionSource<bool>? _exitConfirmation;
    private IInputElement? _exitPreviousFocus;

    private GalNet.Game.Controls.GameCanvasHost GameCanvas = null!;
    private Control LanguageSelection = null!;
    private TextBlock StartupStatus = null!;
    private Ursa.Controls.WindowToastManager AudioToast = null!;
    private Control PlayerContent = null!;
    private Control ExitConfirmation = null!;
    private Control ExitCopy = null!;
    private Control ReturnToTitleCopy = null!;
    private Button CancelExitButton = null!;
    private Button ConfirmExitButton = null!;
    private Button ConfirmReturnToTitleButton = null!;
    private bool _initialized;

    protected void InitializeSurface()
    {
        if (_initialized) throw new InvalidOperationException("Player surface behavior is already initialized.");
        GameCanvas = Require<GalNet.Game.Controls.GameCanvasHost>(nameof(GameCanvas));
        LanguageSelection = Require<Control>(nameof(LanguageSelection));
        StartupStatus = Require<TextBlock>(nameof(StartupStatus));
        AudioToast = Require<Ursa.Controls.WindowToastManager>(nameof(AudioToast));
        PlayerContent = Require<Control>(nameof(PlayerContent));
        ExitConfirmation = Require<Control>(nameof(ExitConfirmation));
        ExitCopy = Require<Control>(nameof(ExitCopy));
        ReturnToTitleCopy = Require<Control>(nameof(ReturnToTitleCopy));
        CancelExitButton = Require<Button>(nameof(CancelExitButton));
        ConfirmExitButton = Require<Button>(nameof(ConfirmExitButton));
        ConfirmReturnToTitleButton = Require<Button>(nameof(ConfirmReturnToTitleButton));
        CancelExitButton.Click += OnCancelExit;
        ConfirmExitButton.Click += OnConfirmExit;
        ConfirmReturnToTitleButton.Click += OnConfirmExit;
        _initialized = true;
        AddHandler(KeyDownEvent, OnExitKeyDown, RoutingStrategies.Tunnel);
    }

    private T Require<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Player surface layout requires '{name}' ({typeof(T).Name}) after loading XAML.");

    public void Attach(GameShell shell, LanguageSelectionViewModel languageSelection)
    {
        if (_languageSelection is not null) throw new InvalidOperationException("The player view is already attached.");
        _languageSelection = languageSelection;
        _shell = shell;
        shell.ConfirmReturnToTitleAsync = ConfirmReturnToTitleAsync;
        LanguageSelection.DataContext = languageSelection;
        GameCanvas.GameContent = shell;
    }

    public void Detach()
    {
        CompleteExitConfirmation(false, restorePlayback: false);
        AudioToast.CloseAll();
        if (_audioGame is not null) _audioGame.AudioAnnouncementRequested -= OnAudioAnnouncement;
        _audioGame = null;
        if (_shell is not null) _shell.ConfirmReturnToTitleAsync = null;
        _shell = null;
        GameCanvas.GameContent = null;
        LanguageSelection.DataContext = null;
        _languageSelection = null;
    }

    public void BindAudioAnnouncements(GamePresentationState gameplay)
    {
        if (_audioGame is not null) _audioGame.AudioAnnouncementRequested -= OnAudioAnnouncement;
        _audioGame = gameplay;
        gameplay.AudioAnnouncementRequested += OnAudioAnnouncement;
    }

    private void OnAudioAnnouncement(string name)
    {
        AudioToast.CloseAll();
        // MusicToastTheme spends 0.8 seconds entering, then leaves two seconds to read.
        if (!string.IsNullOrWhiteSpace(name) && _exitConfirmation is null)
            AudioToast.Show(name, NotificationType.Information,
                TimeSpan.FromSeconds(2.8), showIcon: false, showClose: false);
    }

    public Task<bool> ConfirmExitAsync() => ConfirmAsync(returnToTitle: false);
    public Task<bool> ConfirmReturnToTitleAsync() => ConfirmAsync(returnToTitle: true);

    private Task<bool> ConfirmAsync(bool returnToTitle)
    {
        // A confirmation for one action must never authorize a different action.
        if (_exitConfirmation is not null)
            return _returnToTitle == returnToTitle ? _exitConfirmation.Task : Task.FromResult(false);
        _returnToTitle = returnToTitle;
        ExitCopy.IsVisible = !returnToTitle;
        ReturnToTitleCopy.IsVisible = returnToTitle;
        ConfirmExitButton.IsVisible = !returnToTitle;
        ConfirmReturnToTitleButton.IsVisible = returnToTitle;
        _exitConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitPreviousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        _shell?.SetConfirmationOpen(true);
        AudioToast.CloseAll();
        PlayerContent.IsEnabled = false;
        ExitConfirmation.IsVisible = true;
        CancelExitButton.Focus();
        return _exitConfirmation.Task;
    }

    private void OnCancelExit(object? sender, RoutedEventArgs e) => CompleteExitConfirmation(false);
    private void OnConfirmExit(object? sender, RoutedEventArgs e) => CompleteExitConfirmation(true);
    public bool CancelExitConfirmation()
    {
        if (_exitConfirmation is null) return false;
        CompleteExitConfirmation(false);
        return true;
    }
    private void OnExitKeyDown(object? sender, KeyEventArgs e)
    {
        if (_exitConfirmation is null || e.Key != Key.Escape) return;
        e.Handled = true;
        CompleteExitConfirmation(false);
    }

    private void CompleteExitConfirmation(bool confirmed, bool restorePlayback = true)
    {
        var completion = _exitConfirmation;
        _exitConfirmation = null;
        if (completion is not null) _shell?.SetConfirmationOpen(false, restorePlayback && !confirmed);
        ExitConfirmation.IsVisible = false;
        PlayerContent.IsEnabled = true;
        _exitPreviousFocus?.Focus();
        _exitPreviousFocus = null;
        completion?.TrySetResult(confirmed);
    }

    public async Task<string?> SelectLanguageAsync(string preferred, CancellationToken cancellationToken)
    {
        var selection = _languageSelection ?? throw new InvalidOperationException("The player view is not attached.");
        StartupStatus.IsVisible = false;
        LanguageSelection.IsVisible = true;
        try { return await selection.ChooseAsync(preferred, cancellationToken); }
        finally { LanguageSelection.IsVisible = false; }
    }

    public void CompleteStartup(string? error = null)
    {
        StartupStatus.IsVisible = error is not null;
        if (error is not null) StartupStatus.Text = error;
        GameCanvas.IsVisible = error is null;
    }
}
