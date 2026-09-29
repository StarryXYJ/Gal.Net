using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.Page;
using GalNet.Sample.Avalonia.Debug;
using Microsoft.Extensions.DependencyInjection;
using Ursa.Controls;

namespace GalNet.Sample.Avalonia.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly IServiceProvider _services;
    private readonly SampleDebugLogStore? _debugLogStore;
    private readonly SampleDebugActions? _debugActions;
    private DebugLogWindow? _debugLogWindow;
    private bool _isClearingPlayerState;
    private bool _isReloadingGameResources;

    public MainWindow(GameShell shell, IServiceProvider services)
    {
        _services = services;
        _debugLogStore = services.GetService<SampleDebugLogStore>();
        _debugActions = services.GetService<SampleDebugActions>();
        InitializeComponent();
        GameCanvas.GameContent = shell;
        DebugMenu.IsVisible = _debugLogStore is not null;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private async void OnClearPlayerStateClick(object? sender, RoutedEventArgs e)
    {
        if (_debugActions is null || _isClearingPlayerState)
            return;

        var confirmation = await MessageBox.ShowAsync(
            this,
            "这会停止当前游戏，删除所有存档和阅读进度，并将所有玩家变量还原为默认值。此操作无法撤销。",
            "清空游戏数据",
            button: MessageBoxButton.OKCancel);
        if (confirmation != MessageBoxResult.OK)
            return;

        _isClearingPlayerState = true;
        try
        {
            await _debugActions.ClearPlayerStateAsync();
        }
        catch (Exception exception)
        {
            await MessageBox.ShowAsync(this, exception.Message, "清空游戏数据失败", button: MessageBoxButton.OK);
        }
        finally
        {
            _isClearingPlayerState = false;
        }
    }

    private async void OnShowDebugLogClick(object? sender, RoutedEventArgs e) => await ShowDebugLogAsync();

    private async void OnReloadGameResourcesClick(object? sender, RoutedEventArgs e)
    {
        if (_debugActions is null || _isReloadingGameResources)
            return;

        _isReloadingGameResources = true;
        try
        {
            await _debugActions.ReloadGameResourcesAsync();
        }
        catch (Exception exception)
        {
            await MessageBox.ShowAsync(this, exception.Message, "重新加载游戏资源失败", button: MessageBoxButton.OK);
        }
        finally
        {
            _isReloadingGameResources = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_debugLogStore is null || e.Key != Key.L || e.KeyModifiers != KeyModifiers.None)
            return;

        e.Handled = true;
        _ = ShowDebugLogAsync();
    }

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
}
