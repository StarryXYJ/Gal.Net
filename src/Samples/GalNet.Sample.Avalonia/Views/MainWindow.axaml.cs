using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.Page;
using GalNet.Sample.Avalonia.Debug;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly IServiceProvider _services;
    private readonly SampleDebugLogStore? _debugLogStore;
    private readonly SampleDebugViewModel? _debugViewModel;
    private readonly LanguageSelectionViewModel _languageSelectionViewModel;
    private DebugLogWindow? _debugLogWindow;

    public MainWindow(GameShell shell, IServiceProvider services)
    {
        _services = services;
        _debugLogStore = services.GetService<SampleDebugLogStore>();
        _debugViewModel = services.GetService<SampleDebugViewModel>();
        _languageSelectionViewModel = services.GetRequiredService<LanguageSelectionViewModel>();
        InitializeComponent();
        services.GetRequiredService<SamplePlayerSettingsService>().BindWindow(this);
        PlayerRoot.Styles.Add(SampleCustomization.PlayerTheme.CreateStyles());
        LanguageSelection.DataContext = _languageSelectionViewModel;
        GameCanvas.GameContent = shell;
        DebugMenu.IsVisible = false;
        DebugMenu.DataContext = _debugViewModel;
        if (_debugViewModel is not null)
        {
            _debugViewModel.LogRequested += OnLogRequested;
            _debugViewModel.ConfirmationRequested += ConfirmAsync;
            _debugViewModel.NotificationRequested += NotifyAsync;
        }
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Closed += OnClosed;
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
        DebugMenu.IsVisible = error is null && _debugLogStore is not null && _debugViewModel is not null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_debugLogStore is null || e.Key != Key.L || e.KeyModifiers != KeyModifiers.None)
            return;

        e.Handled = true;
        _debugViewModel?.ShowLogCommand.Execute(null);
    }

    private void OnLogRequested() => _ = ShowDebugLogAsync();

    private async Task<bool> ConfirmAsync(SampleDebugPrompt prompt) =>
        await MessageBox.ShowAsync(this, prompt.Message, prompt.Title, button: MessageBoxButton.OKCancel) == MessageBoxResult.OK;

    private Task<MessageBoxResult> NotifyAsync(SampleDebugNotification notification) =>
        MessageBox.ShowAsync(this, notification.Message, notification.Title, button: MessageBoxButton.OK);

    private Task ShowDebugLogAsync()
    {
        if (_debugLogStore is null)
            return Task.CompletedTask;

        if (_debugLogWindow is { IsVisible: true })
        {
            _debugLogWindow.Activate();
            return Task.CompletedTask;
        }

        var window = _services.GetRequiredService<DebugLogWindow>();
        _debugLogWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_debugLogWindow, window))
                _debugLogWindow = null;
        };
        window.Show(this);
        return Task.CompletedTask;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _services.GetRequiredService<SamplePlayerSettingsService>().BindWindow(null);
        if (_debugViewModel is null)
            return;

        _debugViewModel.LogRequested -= OnLogRequested;
        _debugViewModel.ConfirmationRequested -= ConfirmAsync;
        _debugViewModel.NotificationRequested -= NotifyAsync;
    }
}
