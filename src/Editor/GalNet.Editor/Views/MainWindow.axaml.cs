using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using GalNet.Editor.Abstraction.Services;
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
    private readonly IProjectService _projects;
    private bool _closeApproved;
    private bool _closing;

    public MainWindow(MainWindowViewModel viewModel, IEditorViewFactory viewFactory, IProjectService projects)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigation = viewModel.Navigation;
        _viewFactory = viewFactory;
        _projects = projects;
        _toastManager = new WindowToastManager(this);

        _navigation.CurrentPageChanged += OnCurrentPageChanged;
        Closed += OnClosed;
        Closing += OnClosing;
#if BENCHMARK
        Opened += (_, _) => GalNet.Diagnostics.CpuProfiler.Shared.Mark("Editor.Window.Opened");
        Loaded += (_, _) => GalNet.Diagnostics.CpuProfiler.Shared.Mark("Editor.Window.Loaded");
#endif

        Log.Information("=== GalNet Editor starting ===");
#if BENCHMARK
        using var startupMeasurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.NavigateStartup");
#endif
        _navigation.NavigateTo<StartupPageViewModel>();
    }

    public void ShowSuccessToast(string message) =>
        _toastManager.Show(new Toast(message), showIcon: false, showClose: true, type: NotificationType.Success);

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closeApproved || _projects.Current is null) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            await _projects.CloseAsync();
            _closeApproved = true;
            Close();
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception failure)
        {
            Log.Error(failure, "Could not close the project");
            _toastManager.Show(new Toast(GalNet.Editor.Services.EditorLocalizationService.Current is { } localization ? GalNet.Editor.Services.EditorText.Error(failure).Render(localization) : failure.Message), showClose: true, type: NotificationType.Error);
        }
        finally { _closing = false; }
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        _navigation.CurrentPageChanged -= OnCurrentPageChanged;
        Closed -= OnClosed;
        Closing -= OnClosing;
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
