using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using GalNet.Core.Services;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Serilog;
using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly INavigationService _navigation;
    private readonly IEditorViewFactory _viewFactory;
    private readonly WindowToastManager _toastManager;

    public MainWindow(MainWindowViewModel viewModel, IEditorViewFactory viewFactory)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigation = viewModel.Navigation;
        _viewFactory = viewFactory;
        _toastManager = new WindowToastManager(this);

        _navigation.CurrentPageChanged += OnCurrentPageChanged;
        Closed += OnClosed;

        Loaded += (_, _) =>
        {
            Log.Information("=== GalNet Editor starting ===");
            _navigation.NavigateTo<StartupPageViewModel>();
        };
    }

    public void ShowSuccessToast(string message) =>
        _toastManager.Show(new Toast(message), showIcon: false, showClose: true, type: NotificationType.Success);

    private void OnClosed(object? sender, System.EventArgs e)
    {
        _navigation.CurrentPageChanged -= OnCurrentPageChanged;
        Closed -= OnClosed;
    }

    private void OnCurrentPageChanged(object? page)
    {
        if (page is EditorPageViewModel)
            WindowState = WindowState.Maximized;

        if (page == null)
        {
            PageHost.Content = null;
            return;
        }

        var viewType = _navigation.GetRegisteredViewType(page.GetType());
        if (viewType == null)
        {
            Log.Warning("No view registered for {ViewModelType}", page.GetType().Name);
            PageHost.Content = null;
            return;
        }

        PageHost.Content = _viewFactory.CreateView(viewType, page);
    }
}
