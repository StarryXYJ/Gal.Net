using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GalNet.Avalonia.Debugging.Views;

/// <summary>Window-free debug menu, logs and confirmations around a host's game surface.</summary>
public partial class DebugToolsOverlay : UserControl, IDisposable
{
    private readonly GameDebugViewModel _model;
    private readonly DebugLogView _logs;
    private TaskCompletionSource<bool>? _prompt;
    private bool _disposed;
    private bool _toolsEnabled;

    public DebugToolsOverlay(GameDebugViewModel model, DebugLogView logs)
    {
        _model = model;
        _logs = logs;
        InitializeComponent();
        DebugMenu.DataContext = model;
        LogContent.Content = logs;
        _logs.CloseRequested += HideLogs;
        _model.LogRequested += ShowLogs;
        _model.ConfirmationRequested += ConfirmAsync;
        _model.NotificationRequested += NotifyAsync;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        ToolsEnabled = false;
    }

    public Control? GameContent
    {
        get => GameContentHost.Content as Control;
        set => GameContentHost.Content = value;
    }

    public bool ToolsEnabled
    {
        get => _toolsEnabled;
        set { _toolsEnabled = value; DebugMenu.IsEnabled = value && _prompt is null; }
    }

    public bool TryHandleBack()
    {
        if (_disposed) return false;
        if (_prompt is not null) { CompletePrompt(false); return true; }
        if (LogContent.IsVisible) { HideLogs(); return true; }
        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && TryHandleBack()) e.Handled = true;
        else if (ToolsEnabled && e.Key == Key.L && e.KeyModifiers == KeyModifiers.None)
        {
            ShowLogs();
            e.Handled = true;
        }
    }

    private void ShowLogs()
    {
        if (_disposed || !ToolsEnabled) return;
        LogContent.IsVisible = true;
        GameContentHost.IsEnabled = false;
    }
    private void HideLogs()
    {
        LogContent.IsVisible = false;
        GameContentHost.IsEnabled = _prompt is null;
    }
    private Task<bool> ConfirmAsync(GameDebugPrompt prompt) => ShowPrompt(prompt.Message, prompt.Title, true);
    private async Task NotifyAsync(GameDebugNotification notification) =>
        await ShowPrompt(notification.Message, notification.Title, false);

    private Task<bool> ShowPrompt(string message, string title, bool confirm)
    {
        if (_disposed) return Task.FromResult(false);
        CompletePrompt(false);
        _prompt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PromptTitle.Text = title;
        PromptMessage.Text = message;
        CancelPromptButton.IsVisible = confirm;
        PromptLayer.IsVisible = true;
        GameContentHost.IsEnabled = false;
        DebugMenu.IsEnabled = false;
        CancelPromptButton.Focus();
        return _prompt.Task;
    }

    private void CompletePrompt(bool confirmed)
    {
        var prompt = _prompt;
        _prompt = null;
        PromptLayer.IsVisible = false;
        GameContentHost.IsEnabled = !LogContent.IsVisible;
        DebugMenu.IsEnabled = _toolsEnabled;
        prompt?.TrySetResult(confirmed);
    }

    private void OnCancelPrompt(object? sender, RoutedEventArgs e) => CompletePrompt(false);
    private void OnConfirmPrompt(object? sender, RoutedEventArgs e) => CompletePrompt(true);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ToolsEnabled = false;
        CompletePrompt(false);
        _logs.CloseRequested -= HideLogs;
        _model.LogRequested -= ShowLogs;
        _model.ConfirmationRequested -= ConfirmAsync;
        _model.NotificationRequested -= NotifyAsync;
        DebugMenu.DataContext = null;
        LogContent.Content = null;
        GameContent = null;
        GC.SuppressFinalize(this);
    }
}
