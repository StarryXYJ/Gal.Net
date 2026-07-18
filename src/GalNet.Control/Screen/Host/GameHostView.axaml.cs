using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Core.Services;
using Serilog;

namespace GalNet.Control.Screen.Host;

/// <summary>
/// Root host view — owns navigation, modal dialogs, and toast notifications.
/// Implements INavigationHost for ViewModel access.
/// </summary>
public partial class GameHostView : UserControl, INavigationHost
{
    private readonly Dictionary<string, Func<object?>> _navigationMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<object?>> _modalMap = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _toastCts;

    public GameHostView()
    {
        InitializeComponent();
        Log.Debug("GameHostView constructed");
    }

    /// <summary>Register a navigation target view factory.</summary>
    public void RegisterNavigateTo(string key, Func<object?> viewFactory)
    {
        _navigationMap[key] = viewFactory;
    }

    /// <summary>Register a modal view factory.</summary>
    public void RegisterModal(string key, Func<object?> viewFactory)
    {
        _modalMap[key] = viewFactory;
    }

    // ═══════════════════════════════════════════════════════
    //  INavigationHost
    // ═══════════════════════════════════════════════════════

    public void NavigateTo(string key, object? parameter = null)
    {
        Log.Debug("NavigateTo: key={Key}", key);

        if (!_navigationMap.TryGetValue(key, out var factory))
        {
            Log.Warning("NavigateTo: no factory registered for key={Key}", key);
            return;
        }

        var view = factory();
        if (view is Avalonia.Controls.Control ctrl)
        {
            MainContent.Content = ctrl;
            Log.Debug("NavigateTo: set content type={Type}", ctrl.GetType().Name);
        }
    }

    public void ShowModal(string key, object? parameter = null)
    {
        Log.Debug("ShowModal: key={Key}", key);

        if (!_modalMap.TryGetValue(key, out var factory))
        {
            Log.Warning("ShowModal: no factory registered for key={Key}", key);
            return;
        }

        var view = factory();
        if (view is Avalonia.Controls.Control ctrl)
        {
            ModalOverlay.Content = ctrl;
            ModalOverlay.IsVisible = true;
            Log.Debug("ShowModal: displayed type={Type}", ctrl.GetType().Name);
        }
    }

    public void CloseModal()
    {
        Log.Debug("CloseModal");
        ModalOverlay.IsVisible = false;
        ModalOverlay.Content = null;
    }

    public async void ShowToast(string message)
    {
        Log.Debug("ShowToast: {Message}", message);

        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();

        ToastText.Text = message;
        ToastBar.IsVisible = true;
        ToastBar.Opacity = 1;

        try
        {
            await Task.Delay(2500, _toastCts.Token);
            await Dispatcher.UIThread.InvokeAsync(() => ToastBar.IsVisible = false);
        }
        catch (TaskCanceledException) { }
    }
}
