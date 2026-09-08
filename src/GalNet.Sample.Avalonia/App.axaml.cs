using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Avalonia;

public partial class App : Application
{
    internal static GameLaunchOptions LaunchOptions { get; set; } = new(null, null);
    private ServiceProvider? _rootServices;
    private IServiceScope? _gameScope;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddAvaloniaGameViewPages();
            services.AddScoped<SampleGameSessionService>();
            services.AddScoped<IGameSessionService>(sp => sp.GetRequiredService<SampleGameSessionService>());
            services.AddTransient<MainWindow>();
            _rootServices = services.BuildServiceProvider();
            _gameScope = _rootServices.CreateScope();
            var scopedServices = _gameScope.ServiceProvider;
            scopedServices.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>();
            var mainWindow = scopedServices.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += async (_, _) => await scopedServices.GetRequiredService<SampleGameSessionService>().InitializeAsync(LaunchOptions);
            mainWindow.Closed += (_, _) =>
            {
                _gameScope?.Dispose();
                _rootServices?.Dispose();
                _gameScope = null;
                _rootServices = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
