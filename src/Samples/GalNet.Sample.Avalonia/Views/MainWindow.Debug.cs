using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.Debugging;
using GalNet.Avalonia.Debugging.Views;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow
{
    private IServiceProvider _services = null!;
    private GameDebugLogStore? _debugLogStore;
    private GameDebugViewModel? _debugViewModel;
    private DebugLogWindow? _debugLogWindow;
    private DebugMenuView _debugMenu = null!;

    partial void InitializeDebug(IServiceProvider services)
    {
        _services = services;
        _debugLogStore = services.GetService<GameDebugLogStore>();
        _debugViewModel = services.GetService<GameDebugViewModel>();
        _debugMenu = new DebugMenuView();
        LeftContent = _debugMenu;
        _debugMenu.IsVisible = false;
        _debugMenu.DataContext = _debugViewModel;
        if (_debugViewModel is not null)
        {
            _debugViewModel.LogRequested += OnLogRequested;
            _debugViewModel.ConfirmationRequested += ConfirmAsync;
            _debugViewModel.NotificationRequested += NotifyAsync;
        }
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    partial void CompleteDebugStartup(string? error) =>
        _debugMenu.IsVisible = error is null && _debugLogStore is not null && _debugViewModel is not null;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_debugLogStore is null || e.Key != Key.L || e.KeyModifiers != KeyModifiers.None)
            return;

        e.Handled = true;
        _debugViewModel?.ShowLogCommand.Execute(null);
    }

    private void OnLogRequested() => _ = ShowDebugLogAsync();

    private async Task<bool> ConfirmAsync(GameDebugPrompt prompt) =>
        await MessageBox.ShowAsync(this, prompt.Message, prompt.Title, button: MessageBoxButton.OKCancel) == MessageBoxResult.OK;

    private Task<MessageBoxResult> NotifyAsync(GameDebugNotification notification) =>
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
        window.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        _debugLogWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_debugLogWindow, window))
                _debugLogWindow = null;
        };
        window.Show(this);
        return Task.CompletedTask;
    }

    partial void DetachDebugBindings()
    {
        if (_debugViewModel is null)
            return;

        _debugViewModel.LogRequested -= OnLogRequested;
        _debugViewModel.ConfirmationRequested -= ConfirmAsync;
        _debugViewModel.NotificationRequested -= NotifyAsync;
    }
}
