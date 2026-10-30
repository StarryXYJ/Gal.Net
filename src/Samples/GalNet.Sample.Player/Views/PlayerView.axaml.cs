using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.Page;
using GalNet.Sample.Player.ViewModels;

namespace GalNet.Sample.Player.Views;

/// <summary>The shared player surface; the platform owns its window and session lifetime.</summary>
public partial class PlayerView : UserControl
{
    private LanguageSelectionViewModel? _languageSelection;
    private TaskCompletionSource<bool>? _exitConfirmation;
    private IInputElement? _exitPreviousFocus;

    public PlayerView()
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(SamplePlayerServices.PlayerPalette.CreateResources());
        PlayerRoot.Styles.Add(SamplePlayerServices.PlayerTheme.CreateStyles());
        AddHandler(KeyDownEvent, OnExitKeyDown, RoutingStrategies.Tunnel);
    }

    public void Attach(GameShell shell, LanguageSelectionViewModel languageSelection)
    {
        if (_languageSelection is not null) throw new InvalidOperationException("The player view is already attached.");
        _languageSelection = languageSelection;
        LanguageSelection.DataContext = languageSelection;
        GameCanvas.GameContent = shell;
    }

    public void Detach()
    {
        CompleteExitConfirmation(false);
        GameCanvas.GameContent = null;
        LanguageSelection.DataContext = null;
        _languageSelection = null;
    }

    public Task<bool> ConfirmExitAsync()
    {
        if (_exitConfirmation is not null) return _exitConfirmation.Task;
        _exitConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitPreviousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        PlayerContent.IsEnabled = false;
        ExitConfirmation.IsVisible = true;
        CancelExitButton.Focus();
        return _exitConfirmation.Task;
    }

    private void OnCancelExit(object? sender, RoutedEventArgs e) => CompleteExitConfirmation(false);
    private void OnConfirmExit(object? sender, RoutedEventArgs e) => CompleteExitConfirmation(true);
    private void OnExitKeyDown(object? sender, KeyEventArgs e)
    {
        if (_exitConfirmation is null || e.Key != Key.Escape) return;
        e.Handled = true;
        CompleteExitConfirmation(false);
    }

    private void CompleteExitConfirmation(bool confirmed)
    {
        var completion = _exitConfirmation;
        _exitConfirmation = null;
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
