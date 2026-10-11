using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Avalonia.GameView.Composition;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Logging;
using GalNet.Sample.Avalonia.Debug;
using GalNet.Sample.Avalonia.Services;
using GalNet.Sample.Avalonia.ViewModels;
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
                .AddSingleton(_ => CreateLocalizationService())
                .AddScoped<SamplePlayerSettingsService>()
                .AddScoped<SampleStartupService>()
                .AddScoped<LanguageSelectionViewModel>()
                .AddScoped<IPlayerSettingsService>(provider => provider.GetRequiredService<SamplePlayerSettingsService>())
                .AddScoped<SampleGameSessionService>()
                .AddScoped<IGameSessionService>(sp => sp.GetRequiredService<SampleGameSessionService>())
                .AddSampleDebug()
                .AddTransient<MainWindow>();

            _rootServices = services.BuildServiceProvider();
            _gameScope = _rootServices.CreateAsyncScope();
            var scopedServices = _gameScope.Value.ServiceProvider;
            var localization = scopedServices.GetRequiredService<SampleLocalizationService>();
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = localization.GetString;

            scopedServices.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>(NavigationTransition.None);

            var mainWindow = scopedServices.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            var startupCancellation = new CancellationTokenSource();
            mainWindow.Opened += async (_, _) =>
            {
                try
                {
                    var session = scopedServices.GetRequiredService<SampleGameSessionService>();
                    var ready = await session.InitializeAsync(LaunchOptions, mainWindow.SelectLanguageAsync, startupCancellation.Token);
                    if (!startupCancellation.IsCancellationRequested)
                        mainWindow.CompleteStartup(ready ? null : session.StatusMessage);
                }
                catch (OperationCanceledException)
                {
                    if (!startupCancellation.IsCancellationRequested) mainWindow.Close();
                }
                catch (Exception error)
                {
                    GameLog.Logger.Error(error, "Startup flow failed");
                    if (!startupCancellation.IsCancellationRequested) mainWindow.CompleteStartup(error.Message);
                }
            };
            var closingInProgress = false;
            var allowClose = false;
            mainWindow.Closing += async (_, eventArgs) =>
            {
                if (allowClose) return;

                eventArgs.Cancel = true;
                if (closingInProgress) return;

                closingInProgress = true;
                startupCancellation.Cancel();
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
                    startupCancellation.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static SampleLocalizationService CreateLocalizationService()
    {
        var cultureService = new CultureService();
        var provider = new JsonLocalizationProvider();
        provider.Initialize(new JsonLocalizationProviderOptions
        {
            BasePath = "Localization",
            UseEmbeddedResources = true,
            Assembly = typeof(App).Assembly
        });
        cultureService.RegisterProvider(provider);
        cultureService.SetCulture("en-US");
        LocalizationService.Initialize(cultureService);
        return SampleLocalizationService.Create(cultureService, [provider]);
    }
}
