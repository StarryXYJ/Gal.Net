#if GALNET_GAME_DEBUG
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.Debugging;
using GalNet.Avalonia.Debugging.Views;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class MainWindow
{
    private IServiceProvider? _debugServices;
    private GameDebugViewModel? _debugModel;
    private DebugLogWindow? _debugLogWindow;

    internal void AttachDebugServices(IServiceProvider services)
    {
        DetachDebugServices();
        _debugServices = services;
        _debugModel = services.GetRequiredService<GameDebugViewModel>();
        LeftContent = new DebugMenuView { DataContext = _debugModel };
        _debugModel.LogRequested += ShowDebugLog;
        _debugModel.ConfirmationRequested += ConfirmDebugAsync;
        _debugModel.NotificationRequested += NotifyDebugAsync;
        AddHandler(KeyDownEvent, OnDebugKeyDown, RoutingStrategies.Tunnel);
    }

    internal void DetachDebugServices()
    {
        if (_debugModel is not null)
        {
            _debugModel.LogRequested -= ShowDebugLog;
            _debugModel.ConfirmationRequested -= ConfirmDebugAsync;
            _debugModel.NotificationRequested -= NotifyDebugAsync;
        }
        RemoveHandler(KeyDownEvent, OnDebugKeyDown);
        _debugLogWindow?.Close();
        _debugLogWindow = null;
        if (LeftContent is DebugMenuView menu) menu.DataContext = null;
        LeftContent = null;
        _debugModel = null;
        _debugServices = null;
    }

    private void OnDebugKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.L || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        _debugModel?.ShowLogCommand.Execute(null);
    }

    private void ShowDebugLog()
    {
        if (_debugServices is null) return;
        if (_debugLogWindow is { IsVisible: true })
        {
            _debugLogWindow.Activate();
            return;
        }
        var window = _debugServices.GetRequiredService<DebugLogWindow>();
        window.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        _debugLogWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_debugLogWindow, window)) _debugLogWindow = null;
        };
        window.Show(this);
    }

    private async Task<bool> ConfirmDebugAsync(GameDebugPrompt prompt) =>
        await MessageBox.ShowAsync(this, prompt.Message, prompt.Title, button: MessageBoxButton.OKCancel) == MessageBoxResult.OK;

    private Task<MessageBoxResult> NotifyDebugAsync(GameDebugNotification notification) =>
        MessageBox.ShowAsync(this, notification.Message, notification.Title, button: MessageBoxButton.OK);

    partial void DetachDebugBindings() => DetachDebugServices();
}
#endif
