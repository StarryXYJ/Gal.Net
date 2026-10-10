using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Avalonia;

public partial class App : Application
{
    internal static GameLaunchOptions LaunchOptions { get; set; } = new(null, null);
    private ServiceProvider? _rootServices;
    private AsyncServiceScope? _gameScope;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddAvaloniaGameViewPages()
                .AddScoped<SampleGameSessionService>()
                .AddScoped<IGameSessionService>(sp => sp.GetRequiredService<SampleGameSessionService>())
                .AddSampleDebug()
                .AddTransient<MainWindow>();

            _rootServices = services.BuildServiceProvider();
            _gameScope = _rootServices.CreateAsyncScope();
            var scopedServices = _gameScope.Value.ServiceProvider;

            scopedServices.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>(NavigationTransition.None);

            var mainWindow = scopedServices.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Opened += async (_, _) => await scopedServices.GetRequiredService<SampleGameSessionService>().InitializeAsync(LaunchOptions);
            var closingInProgress = false;
            var allowClose = false;
            mainWindow.Closing += async (_, eventArgs) =>
            {
                if (allowClose) return;

                eventArgs.Cancel = true;
                if (closingInProgress) return;

                closingInProgress = true;
                try
                {
                    try
                    {
                        await scopedServices.GetRequiredService<SampleGameSessionService>().StopAsync();
                    }
                    finally
                    {
                        try { if (_gameScope is { } scope) await scope.DisposeAsync(); }
                        finally
                        {
                            _gameScope = null;
                            if (_rootServices is not null) await _rootServices.DisposeAsync();
                            _rootServices = null;
                        }
                    }
                }
                catch (Exception error) { GameLog.Logger.Error(error, "Game host shutdown failed"); }
                finally
                {
                    allowClose = true;
                    mainWindow.Close();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
