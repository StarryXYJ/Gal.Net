using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Sample.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly IAvaloniaPlayerSettings _settings;
    private bool _bindingsDetached;
    private readonly LanguageSelectionViewModel _languageSelectionViewModel;
    private TaskCompletionSource<bool>? _exitConfirmation;
    private IInputElement? _exitPreviousFocus;

    public MainWindow(GameShell shell, IServiceProvider services)
    {
        _languageSelectionViewModel = services.GetRequiredService<LanguageSelectionViewModel>();
        InitializeComponent();
        _settings = services.GetRequiredService<IAvaloniaPlayerSettings>();
        _settings.BindWindow(this);
        Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        PlayerRoot.Styles.Add(SampleCustomization.PlayerTheme.CreateStyles());
        LanguageSelection.DataContext = _languageSelectionViewModel;
        GameCanvas.GameContent = shell;
        InitializeDebug(services);
        Closed += OnClosed;
        AddHandler(KeyDownEvent, OnExitKeyDown, RoutingStrategies.Tunnel);
    }

    public Task<bool> ConfirmExitAsync()
    {
        if (_exitConfirmation is not null) return _exitConfirmation.Task;
        _exitConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitPreviousFocus = FocusManager?.GetFocusedElement();
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
        StartupStatus.IsVisible = false;
        LanguageSelection.IsVisible = true;
        try { return await _languageSelectionViewModel.ChooseAsync(preferred, cancellationToken); }
        finally { LanguageSelection.IsVisible = false; }
    }

    public void CompleteStartup(string? error = null)
    {
        StartupStatus.IsVisible = error is not null;
        if (error is not null) StartupStatus.Text = error;
        GameCanvas.IsVisible = error is null;
        CompleteDebugStartup(error);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompleteExitConfirmation(false);
        DetachHostBindings();
        DetachDebugBindings();
    }

    partial void InitializeDebug(IServiceProvider services);
    partial void CompleteDebugStartup(string? error);
    partial void DetachDebugBindings();

    internal void DetachHostBindings()
    {
        if (_bindingsDetached) return;
        _bindingsDetached = true;
        _settings.BindWindow(null);
    }
}
