using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Logging;
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
#if BENCHMARK
        using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Sample.Xaml");
#endif
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if BENCHMARK
        using var frameworkMeasurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Sample.FrameworkInitialization");
#endif
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
#if BENCHMARK
            using (GalNet.Diagnostics.CpuProfiler.Shared.Measure("Sample.RegisterServices"))
#endif
                SampleCustomization.ConfigureServices(services);

#if BENCHMARK
            using (GalNet.Diagnostics.CpuProfiler.Shared.Measure("Sample.BuildServiceProvider"))
#endif
                _rootServices = services.BuildServiceProvider();
            _gameScope = _rootServices.CreateAsyncScope();
            var scopedServices = _gameScope.Value.ServiceProvider;
            var localization = scopedServices.GetRequiredService<SampleLocalizationService>();
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = localization.GetString;

            scopedServices.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>(NavigationTransition.None);

            MainWindow mainWindow;
#if BENCHMARK
            using (GalNet.Diagnostics.CpuProfiler.Shared.Measure("Sample.CreateMainWindow"))
#endif
                mainWindow = scopedServices.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            var startupCancellation = new CancellationTokenSource();
            mainWindow.Opened += async (_, _) =>
            {
                try
                {
#if BENCHMARK
                    using var startupMeasurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Sample.InitializeSession");
#endif
                    var session = scopedServices.GetRequiredService<SampleGameSessionService>();
                    var ready = await session.InitializeAsync(LaunchOptions, mainWindow.SelectLanguageAsync, startupCancellation.Token);
                    if (ready && LaunchOptions.DebugInvitation is { } invitation)
                        await scopedServices.GetRequiredService<Debug.SampleExternalDebug>().ConnectAsync(invitation, startupCancellation.Token);
                    if (!startupCancellation.IsCancellationRequested)
                        mainWindow.CompleteStartup(ready ? null : session.StatusMessage);
#if BENCHMARK
                    startupMeasurement.Dispose();
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (GalNet.Diagnostics.StartupBenchmark.Complete(ready)) mainWindow.Close();
                    }, global::Avalonia.Threading.DispatcherPriority.Background);
#endif
                }
                catch (OperationCanceledException)
                {
#if BENCHMARK
                    GalNet.Diagnostics.StartupBenchmark.Complete(false);
#endif
                    if (!startupCancellation.IsCancellationRequested) mainWindow.Close();
                }
                catch (Exception error)
                {
                    GameLog.Logger.Error(error, "Startup flow failed");
                    if (!startupCancellation.IsCancellationRequested) mainWindow.CompleteStartup(error.Message);
#if BENCHMARK
                    if (GalNet.Diagnostics.StartupBenchmark.Complete(false)) mainWindow.Close();
#endif
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
                var shutdown = System.Diagnostics.Stopwatch.StartNew();
                GameLog.Logger.Information("Game host shutdown started");
                startupCancellation.Cancel();
                try
                {
                    try
                    {
                        await scopedServices.GetRequiredService<SampleGameSessionService>().StopAsync();
                        GameLog.Logger.Information("Game host stopped after {ElapsedMilliseconds} ms", shutdown.ElapsedMilliseconds);
                    }
                    finally
                    {
                        try
                        {
                            mainWindow.DetachHostBindings();
                            if (_gameScope is { } scope) await scope.DisposeAsync();
                            GameLog.Logger.Information("Game scope disposed after {ElapsedMilliseconds} ms", shutdown.ElapsedMilliseconds);
                        }
                        finally
                        {
                            _gameScope = null;
                            if (_rootServices is not null) await _rootServices.DisposeAsync();
                            GameLog.Logger.Information("Root services disposed after {ElapsedMilliseconds} ms", shutdown.ElapsedMilliseconds);
                            _rootServices = null;
                        }
                    }
                }
                catch (Exception error) { GameLog.Logger.Error(error, "Game host shutdown failed"); }
                finally
                {
                    allowClose = true;
                    GameLog.Logger.Information("Game host cleanup finished after {ElapsedMilliseconds} ms; closing window", shutdown.ElapsedMilliseconds);
                    mainWindow.Close();
                    startupCancellation.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
