using System;
using Avalonia.Controls;
using GalNet.Core.Services;
using GalNet.Editor.ViewModels;
using Serilog;
using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class MainWindow : UrsaWindow
{
    private readonly INavigationService _navigation;
    private readonly IServiceProvider _serviceProvider;

    public MainWindow(MainWindowViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        _navigation = viewModel.Navigation;
        _serviceProvider = serviceProvider;

        _navigation.CurrentPageChanged += OnCurrentPageChanged;

        Loaded += (_, _) =>
        {
            Log.Information("=== GalNet Editor starting ===");
            _navigation.NavigateTo<StartupPageViewModel>();
        };
    }

    private void OnCurrentPageChanged(object? page)
    {
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

        var view = (Avalonia.Controls.Control)(_serviceProvider.GetService(viewType)
                   ?? Activator.CreateInstance(viewType)!);
        view.DataContext = page;
        PageHost.Content = view;
    }
}
