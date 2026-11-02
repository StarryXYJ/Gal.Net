using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using GalNet.Sample.CrossPlatform.ViewModels;
using GalNet.Runtime.Logging;
using GalNet.Sample.CrossPlatform.Views;
using GalNet.Avalonia.GameHost;
using GalNet.Contracts.Builtins;
using GalNet.Sample.CrossPlatform.Presentation;
using GalNet.Storage.FileSystem;
using GalNet.Game.Controls.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;
using Avalonia.Threading;
using GalNet.Sample.CrossPlatform.Opening;
#if GALNET_GAME_DEBUG
using GalNet.Avalonia.Debugging;
using GalNet.Avalonia.Debugging.Views;
using GalNet.Debugging;
using GalNet.Runtime.Engine;
#endif

namespace GalNet.Sample.CrossPlatform;

public partial class App : Application, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _composition = new(1, 1);
    private AvaloniaPlayerHost? _player;
    private ContentControl? _mount;
    private MainWindow? _window;
    private Task? _startup;
    private bool _stopping;
    private Serilog.ILogger? _logger;
    private int _generation;
    private Task? _shutdown;
    private bool _platformActive = true;
    private bool _audioAvailable = true;
    private static ProjectOrientationPolicy _orientationPolicy = ProjectOrientationPolicy.Unforced;
    private static ProjectDefaultOrientation _defaultOrientation = ProjectDefaultOrientation.Landscape;
    private DesktopWindowPlacement? _windowPlacement;
    private OpeningSequence? _opening;
    private Grid? _startupSurface;
    private readonly CultureInfo _systemCulture = CultureInfo.CurrentUICulture;
#if GALNET_GAME_DEBUG
    private DebugToolsOverlay? _debugSurface;
    public static DebugInvitation? DebugInvitation { get; set; }
#endif

    public static Func<CancellationToken, Task<PlayerLaunchPaths>> PrepareLaunchAsync { get; set; } = _ =>
    {
        var content = ApplicationPackageLayout.GetContentDirectory(ApplicationPackageLayout.FindApplicationRoot(AppContext.BaseDirectory));
        return Task.FromResult(new PlayerLaunchPaths(content, SampleCustomization.GetProfileDirectory(content)));
    };

    public static string? DesktopWindowPlacementPath { get; set; }

    public Action? ExitRequested { get; set; }
    public static Action<IServiceCollection>? ConfigurePlatformServices { get; set; }
    public static Func<ProjectOrientationPolicy, ProjectDefaultOrientation, CancellationToken, Task>? ProjectOrientationHandler { get; set; }
    public static ProjectOrientationPolicy CurrentOrientationPolicy => _orientationPolicy;
    public static ProjectDefaultOrientation CurrentDefaultOrientation => _defaultOrientation;

    public static Task ApplyProjectOrientationAsync(ProjectOrientationPolicy policy, ProjectDefaultOrientation defaultOrientation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _orientationPolicy = policy;
        _defaultOrientation = defaultOrientation;
        return ProjectOrientationHandler?.Invoke(policy, defaultOrientation, cancellationToken) ?? Task.CompletedTask;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { Content = CreateHostView() };
            _window = window;
            ProjectOrientationHandler = ApplyDesktopProjectOrientationAsync;
            if (DesktopWindowPlacementPath is { } placementPath)
                AttachDesktopWindowPlacement(window, placementPath);
            desktop.MainWindow = window;
            var closing = false;
            var allowed = false;
            window.Closing += async (_, args) =>
            {
                if (allowed) return;
                args.Cancel = true;
                if (closing) return;
                closing = true;
                if (_opening is not { IsRunning: true } && _player is not null &&
                    TopLevel.GetTopLevel(_player.View) is not null && !await _player.ConfirmExitAsync()) { closing = false; return; }
                try { await ShutdownAsync(); }
                catch (Exception error) { Console.Error.WriteLine(error); }
                finally { allowed = true; window.Close(); }
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = CreateHostView;
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = CreateHostView();
        base.OnFrameworkInitializationCompleted();
    }

    private async Task ApplyDesktopProjectOrientationAsync(ProjectOrientationPolicy policy,
        ProjectDefaultOrientation defaultOrientation, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() => _window?.ApplyProjectOrientation(policy, defaultOrientation),
            DispatcherPriority.Normal, cancellationToken);
    }

    private ContentControl CreateHostView()
    {
        // Activity recreation moves the existing player rather than starting another session.
        if (_mount is not null) _mount.Content = null;
        Control? surface = _player?.View;
#if GALNET_GAME_DEBUG
        surface = _debugSurface ?? surface;
#endif
        surface = _startupSurface ?? surface;
        ContentControl host = ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            ? new ContentControl
            {
                Content = surface,
                HorizontalContentAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
            }
            : new Ursa.Controls.UrsaView { Content = surface };
        TopLevel.SetAutoSafeAreaPadding(host, true);
        _mount = host;
        TopLevel? top = null;
        host.DetachedFromVisualTree += (_, _) =>
        {
            if (top is not null) top.BackRequested -= OnPlatformBackRequested;
            top = null;
        };
        host.AttachedToVisualTree += async (_, _) =>
        {
            if (_window is null && (top = TopLevel.GetTopLevel(host)) is not null)
            {
                top.BackRequested -= OnPlatformBackRequested;
                top.BackRequested += OnPlatformBackRequested;
                if (OperatingSystem.IsAndroid() && top.InsetsManager is { } insets)
                {
                    insets.DisplayEdgeToEdgePreference = true;
                    insets.IsSystemBarVisible = false;
                }
            }
            _startup ??= StartAsync();
            await _startup;
        };
        return host;
    }

    private void AttachDesktopWindowPlacement(MainWindow window, string placementPath)
    {
        _windowPlacement = new DesktopWindowPlacement(window, DesktopWindowPlacement.ReadFile(placementPath), placement =>
        {
            try { DesktopWindowPlacement.WriteFile(placementPath, placement); }
            catch (Exception error) { Log.Warning(error, "Could not save desktop window placement"); }
        });
    }

    private async void OnPlatformBackRequested(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
#if GALNET_GAME_DEBUG
        if (_debugSurface?.TryHandleBack() == true) return;
#endif
        try { await HandleBackAsync(); }
        catch (Exception error) { GameLog.Logger.Error(error, "Platform back navigation failed"); }
    }

    private async Task StartAsync()
    {
        Task<bool>? initialization = null;
        OpeningSequence? opening = null;
        var generation = _generation;
        using var bootstrapText = SampleCustomization.CreateLocalizationService();
        StartupLocalization.ApplySystemCulture(bootstrapText, _systemCulture.Name);
        try
        {
            await _composition.WaitAsync(_lifetime.Token);
            try
            {
                if (_stopping || _player is not null) return;
                generation = ++_generation;
                opening = ShowOpening(bootstrapText);
                // Give the lightweight opening its first frame before platform installation/composition begins.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, _lifetime.Token);
                var paths = await PrepareEnvironmentAsync(bootstrapText, opening);
                var player = await CreatePlayerAsync(paths, opening, bootstrapText);
                initialization = player.InitializeAsync(paths.ContentDirectory, paths.ProfileDirectory);
            }
            finally { _composition.Release(); }
            // Shutdown must be able to cancel onboarding while initialization awaits user input.
            var ready = await initialization;
            if (_stopping || generation != _generation) return;
            if (ready) await opening.Completion;
            else await opening.DisposeAsync();
            if (_stopping || generation != _generation) return;
            if (ready) _player?.Settings.SetGamepadEnabled(true);
#if GALNET_GAME_DEBUG
            if (_player is not null) _player.Services.GetRequiredService<GameDebugViewModel>().IsGameReady = ready;
            if (_debugSurface is not null) _debugSurface.ToolsEnabled = ready;
            if (ready && DebugInvitation is { } invitation && _player is not null)
                await _player.Services.GetRequiredService<AvaloniaExternalDebugSession>().ConnectAsync(invitation, _lifetime.Token);
#endif
            await ApplyPlatformStateAsync();
        }
        catch (OperationCanceledException) when (_stopping || generation != _generation) { }
        catch (Exception error)
        {
            if (opening is not null)
            {
                try { await opening.DisposeAsync(); }
                catch (Exception cleanupError) { GameLog.Logger.Error(cleanupError, "Opening cleanup failed"); }
            }
            // Content preparation can fail before the profile logger is created.
            if (_logger is null) Console.Error.WriteLine(error);
            GameLog.Logger.Error(error, "Cross-platform player startup failed");
            if (!_stopping && generation == _generation && _mount is not null)
            {
                _startupSurface = null;
                _mount.Content = new TextBlock { Text = error.Message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) };
            }
        }
    }

    private OpeningSequence ShowOpening(AvaloniaPlayerLocalization text)
    {
        var opening = _opening = SampleCustomization.CreateOpeningSequence(text);
        var openingView = new OpeningSequenceView { DataContext = opening };
        var startupSurface = _startupSurface = new Grid();
        startupSurface.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        startupSurface.Styles.Add(SampleCustomization.CreateStyles());
        startupSurface.Background = (global::Avalonia.Media.IBrush)startupSurface.FindResource("GalNet.Brush.Background")!;
        startupSurface.Children.Add(new ProgressBar
        {
            IsIndeterminate = true, Width = 100, Height = 2,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        });
        startupSurface.Children.Add(openingView);
        if (_mount is not null) _mount.Content = startupSurface;
        _ = opening.RunAsync(openingView.PresentAsync, _lifetime.Token);
        return opening;
    }

    private async Task<PlayerLaunchPaths> PrepareEnvironmentAsync(AvaloniaPlayerLocalization text, OpeningSequence opening)
    {
        var paths = await PrepareLaunchAsync(_lifetime.Token);
        await StartupLocalization.ApplyProfileAsync(text, paths.ProfileDirectory, _systemCulture.Name, _lifetime.Token);
        opening.SetProfileDirectory(paths.ProfileDirectory);
        if (_window is not null && _windowPlacement is null)
        {
            var placementPath = Path.Combine(paths.ProfileDirectory, "window-placement.json");
            AttachDesktopWindowPlacement(_window, placementPath);
        }
        Directory.CreateDirectory(Path.Combine(paths.ProfileDirectory, "logs"));
        if (_logger is null)
        {
            var logging = new LoggerConfiguration();
#if GALNET_GAME_DEBUG
            logging.MinimumLevel.Debug().WriteTo.Sink(GameDebugLogStore.Shared);
#else
            logging.MinimumLevel.Information();
#endif
            _logger = logging.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                .WriteTo.File(Path.Combine(paths.ProfileDirectory, "logs/game-.log"), rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14, shared: true, formatProvider: CultureInfo.InvariantCulture).CreateLogger();
            Log.Logger = _logger;
        }
        return paths;
    }

    private async Task<AvaloniaPlayerHost> CreatePlayerAsync(PlayerLaunchPaths paths, OpeningSequence opening, AvaloniaPlayerLocalization text)
    {
        var options = await Task.Run(() => SampleCustomization.CreateHostOptions(
#if GALNET_GAME_DEBUG
            attachRun: AttachDebugRun,
#endif
            screenshotDirectory: title =>
            paths.ScreenshotDirectory ?? SampleScreenshotOutput.GetHostDirectory(title)), _lifetime.Token);
        var player = _player = await AvaloniaPlayerHost.CreateAsync(options, services =>
        {
            SampleCustomization.ConfigureServices(services);
            services.AddSingleton(opening);
            services.AddSingleton(_ =>
            {
                var localization = SampleCustomization.CreateLocalizationService();
                localization.ApplyCulture(text.CultureService.CurrentCulture.Name);
                return localization;
            });
#if GALNET_GAME_DEBUG
            Action? openLogs = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ? null : () =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(paths.ProfileDirectory, "logs")) { UseShellExecute = true });
            services.AddGameDebugTools(SampleCustomization.CreateDebugOptions(_ => RestartAsync(), openLogs));
#endif
            ConfigurePlatformServices?.Invoke(services);
        }, _lifetime.Token);
        var title = player.Services.GetRequiredService<TitlePageViewModel>();
        title.CanExit = !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();
        player.Settings.SetGamepadEnabled(false);
        if (_window is not null) player.BindWindow(_window);
        title.ExitRequested = () =>
        {
            if (_window is not null) _window.Close();
            else _ = ConfirmPlatformExitAsync();
        };
        Control surface = player.View;
#if GALNET_GAME_DEBUG
        if (_window is not null)
            _window.AttachDebugServices(player.Services);
        else
        {
            _debugSurface = player.Services.GetRequiredService<DebugToolsOverlay>();
            _debugSurface.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
            _debugSurface.GameContent = surface;
            surface = _debugSurface;
        }
#endif
        _startupSurface!.Children[0] = surface;
        // The title may render while preparation continues; session-dependent commands remain gated.
        player.Services.GetRequiredService<PlayerView>().CompleteStartup();
        return player;
    }

    public Task SetPlatformActiveAsync(bool active)
    {
        _platformActive = active;
        return ApplyPlatformStateAsync();
    }

#if GALNET_GAME_DEBUG
    private IDisposable AttachDebugRun(GameEngine engine) =>
        (_player ?? throw new InvalidOperationException("The player host is not ready.")).Services
            .GetRequiredService<AvaloniaExternalDebugSession>().Attach(engine);
#endif

    public Task SetAudioAvailableAsync(bool available)
    {
        _audioAvailable = available;
        return ApplyPlatformStateAsync();
    }

    private async Task ApplyPlatformStateAsync()
    {
        if (_stopping) return;
        await _composition.WaitAsync();
        try
        {
            if (_stopping || _player is null) return;
            var active = _platformActive && _audioAvailable;
            _player.View.IsEnabled = active;
            if (_startupSurface is not null) _startupSurface.IsEnabled = active;
            if (!active) await _player.SuspendAsync();
        }
        finally { _composition.Release(); }
    }

    public async Task HandleBackAsync()
    {
        if (_opening is { IsRunning: true } opening)
        {
            opening.SkipRemainingCommand.Execute(null);
            return;
        }
        if (_stopping || _player is null) return;
        if (await _player.HandleBackAsync())
        {
            if (OperatingSystem.IsAndroid())
            {
                await _player.SuspendAsync();
                ExitRequested?.Invoke();
                return;
            }
            if (OperatingSystem.IsIOS()) return;
            await ShutdownAsync();
            ExitRequested?.Invoke();
        }
    }

    private async Task ConfirmPlatformExitAsync()
    {
        try
        {
            if (_stopping || _player is null || !await _player.ConfirmExitAsync()) return;
            await ShutdownAsync();
            ExitRequested?.Invoke();
        }
        catch (Exception error) { GameLog.Logger.Error(error, "Player exit failed"); }
    }

    public async Task InterruptAsync()
    {
        if (_stopping) return;
        await _composition.WaitAsync();
        try { if (!_stopping && _player is not null) await _player.SuspendAsync(); }
        finally { _composition.Release(); }
    }

    public async Task RestartAsync(bool clearPlayerState = false)
    {
        await _composition.WaitAsync(_lifetime.Token);
        try
        {
            ++_generation;
            await ReleasePlayerAsync(clearPlayerState);
        }
        finally { _composition.Release(); }
        _startup = StartAsync();
        await _startup;
    }

    private async Task ReleasePlayerAsync(bool clearPlayerState = false)
    {
        var cleanup = new GalNet.GameHost.ResourceCleanup();
        await cleanup.RunAsync(async () => { if (_opening is not null) await _opening.DisposeAsync(); });
        _opening = null;
        _startupSurface = null;
        if (clearPlayerState && _player is not null)
            await cleanup.RunAsync(() => new(_player.Session.ClearPlayerStateAsync(_lifetime.Token)));
#if GALNET_GAME_DEBUG
        cleanup.Run(() => _window?.DetachDebugServices());
        _debugSurface = null;
#endif
        await cleanup.RunAsync(async () => { if (_player is not null) await _player.DisposeAsync(); });
        _player = null;
        cleanup.Run(() => { if (_mount is not null) _mount.Content = null; });
        cleanup.Run(() => (_logger as IDisposable)?.Dispose());
        _logger = null;
        cleanup.ThrowIfFailed();
    }

    public Task ShutdownAsync() => _shutdown ??= ShutdownCoreAsync();

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new(ShutdownAsync());
    }

    private async Task ShutdownCoreAsync()
    {
        _stopping = true;
        ++_generation;
        _lifetime.Cancel();
        await _composition.WaitAsync();
        try { await ReleasePlayerAsync(); }
        finally
        {
            _composition.Release();
            _lifetime.Dispose();
        }
    }
}
