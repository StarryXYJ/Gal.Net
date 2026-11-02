using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Sample.CrossPlatform.Views;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Sample.CrossPlatform.ViewModels;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Project startup and back/exit policy; the host retains asynchronous scope ownership.</summary>
public sealed class PlayerApplication(PlayerView view, GameShell shell, LanguageSelectionViewModel language,
    GamePresentationState state, IGameNavigationService navigation, TitlePageViewModel title,
    AvaloniaPlayerSettingsService settings,
    Opening.OpeningSequence? opening = null) : IPlayerApplication
{
    private GalNet.Avalonia.GameHost.Settings.FontSizePreset? _appliedFontSize;
    public Control View => view;
    public void Attach()
    {
        view.Attach(shell, language);
        view.AttachResponsiveShell(shell);
        view.BindAudioAnnouncements(state);
        settings.SettingsChanged += ApplyTypography;
        _appliedFontSize = null;
        ApplyTypography();
    }
    public void Detach()
    {
        settings.SettingsChanged -= ApplyTypography;
        view.DetachResponsiveShell();
        view.Detach();
    }
    private void ApplyTypography()
    {
        if (_appliedFontSize == settings.RuntimeSettings.FontSize) return;
        view.ApplyTypography(settings.RuntimeSettings.FontSize);
        _appliedFontSize = settings.RuntimeSettings.FontSize;
    }
    public void ShowTitle() => navigation.ResetTo<TitlePageViewModel>(NavigationTransition.None);
    public Task StartNewGameAsync() => title.StartNewGameCommand.ExecuteAsync(null);
    public Task<bool> ConfirmExitAsync() => view.ConfirmExitAsync();
    public async Task<string?> SelectLanguageAsync(string preferred, CancellationToken cancellationToken)
    {
        if (opening is not null) await opening.Completion.WaitAsync(cancellationToken);
        return await view.SelectLanguageAsync(preferred, cancellationToken);
    }
    public void CompleteStartup(string? errorMessage) => view.CompleteStartup(errorMessage);

    public async Task<bool> HandleBackAsync()
    {
        if (view.CancelExitConfirmation()) return false;
        if (view.CloseToolbarMenu()) return false;
        if (navigation.CurrentViewModel is BackNavigablePageViewModel page && navigation.CanGoBack)
            page.BackCommand.Execute(null);
        else if (navigation.CurrentViewModel is GamePageViewModel game)
            game.OpenSettingsCommand.Execute(null);
        else if (!title.CanExit) return true;
        else return await view.ConfirmExitAsync();
        return false;
    }
}
