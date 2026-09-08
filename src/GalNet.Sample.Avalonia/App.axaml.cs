using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using GalNet.Sample.Avalonia.ViewModels;
using GalNet.Sample.Avalonia.Views;

namespace GalNet.Sample.Avalonia;

public partial class App : Application
{
    internal static GameLaunchOptions LaunchOptions { get; set; } = new(null, null);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += async (_, _) => await viewModel.InitializeAsync(LaunchOptions, mainWindow.GamePage);
            mainWindow.Closed += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
