using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Logging;
using GalNet.Sample.CrossPlatform.Views;
using GalNet.Avalonia.GameHost;
using GalNet.Sample.CrossPlatform.Presentation;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;
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
#if GALNET_GAME_DEBUG
    private DebugToolsOverlay? _debugSurface;
    public static DebugInvitation? DebugInvitation { get; set; }
#endif

    public static Func<CancellationToken, Task<PlayerLaunchPaths>> PrepareLaunchAsync { get; set; } = _ =>
    {
        var content = ApplicationPackageLayout.GetContentDirectory(ApplicationPackageLayout.FindApplicationRoot(AppContext.BaseDirectory));
        return Task.FromResult(new PlayerLaunchPaths(content, SampleCustomization.GetProfileDirectory(content)));
    };

    public Action? ExitRequested { get; set; }
    public static Action<IServiceCollection>? ConfigurePlatformServices { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { Content = CreateHostView() };
            _window = window;
            desktop.MainWindow = window;
            var closing = false;
            var allowed = false;
            window.Closing += async (_, args) =>
            {
                if (allowed) return;
                args.Cancel = true;
                if (closing) return;
                closing = true;
                if (_player is not null && !await _player.View.ConfirmExitAsync()) { closing = false; return; }
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

    private ContentControl CreateHostView()
    {
        // Activity recreation moves the existing player rather than starting another session.
        if (_mount is not null) _mount.Content = null;
        Control? surface = _player?.View;
#if GALNET_GAME_DEBUG
        surface = _debugSurface ?? surface;
#endif
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
        var generation = _generation;
        try
        {
            await _composition.WaitAsync(_lifetime.Token);
            try
            {
                if (_stopping || _player is not null) return;
                generation = ++_generation;
                var paths = await PrepareLaunchAsync(_lifetime.Token);
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
                var options = SampleCustomization.CreateHostOptions(
#if GALNET_GAME_DEBUG
                    attachRun: AttachDebugRun,
#endif
                    screenshotDirectory: title =>
                    paths.ScreenshotDirectory ?? SampleScreenshotOutput.GetHostDirectory(title));
                _player = await AvaloniaPlayerHost.CreateAsync(options, services =>
                {
                    SampleCustomization.ConfigureServices(services);
#if GALNET_GAME_DEBUG
                    Action? openLogs = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ? null : () =>
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(paths.ProfileDirectory, "logs")) { UseShellExecute = true });
                    services.AddGameDebugTools(SampleCustomization.CreateDebugOptions(_ => RestartAsync(), openLogs));
#endif
                    ConfigurePlatformServices?.Invoke(services);
                });
                _player.Services.GetRequiredService<TitlePageViewModel>().CanExit =
                    !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS();
                if (_window is not null) _player.BindWindow(_window);
                _player.Services.GetRequiredService<TitlePageViewModel>().ExitRequested = () =>
                {
                    if (_window is not null) _window.Close();
                    else _ = ConfirmPlatformExitAsync();
                };
                Control surface = _player.View;
#if GALNET_GAME_DEBUG
                if (_window is not null)
                    _window.AttachDebugServices(_player.Services);
                else
                {
                    _debugSurface = _player.Services.GetRequiredService<DebugToolsOverlay>();
                    _debugSurface.Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
                    _debugSurface.GameContent = surface;
                    surface = _debugSurface;
                }
#endif
                if (_mount is not null) _mount.Content = surface;
                initialization = _player.InitializeAsync(paths.ContentDirectory, paths.ProfileDirectory);
            }
            finally { _composition.Release(); }
            // Shutdown must be able to cancel onboarding while initialization awaits user input.
            var ready = await initialization;
            if (_stopping || generation != _generation) return;
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
            // Content preparation can fail before the profile logger is created.
            if (_logger is null) Console.Error.WriteLine(error);
            GameLog.Logger.Error(error, "Cross-platform player startup failed");
            if (!_stopping && generation == _generation && _mount is not null)
                _mount.Content = new TextBlock { Text = error.Message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) };
        }
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
            if (!active) await _player.SuspendAsync();
        }
        finally { _composition.Release(); }
    }

    public async Task HandleBackAsync()
    {
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
            if (_stopping || _player is null || !await _player.View.ConfirmExitAsync()) return;
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
            if (_player is not null)
            {
                if (clearPlayerState) await _player.Session.ClearPlayerStateAsync(_lifetime.Token);
#if GALNET_GAME_DEBUG
                _window?.DetachDebugServices();
#endif
                await _player.DisposeAsync();
                _player = null;
#if GALNET_GAME_DEBUG
                _debugSurface = null;
#endif
            }
            if (_mount is not null) _mount.Content = null;
            if (_logger is IDisposable disposable) disposable.Dispose();
            _logger = null;
        }
        finally { _composition.Release(); }
        _startup = StartAsync();
        await _startup;
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
        try
        {
            if (_player is not null)
            {
#if GALNET_GAME_DEBUG
                _window?.DetachDebugServices();
#endif
                try { await _player.DisposeAsync(); }
                finally { _player = null; }
            }
        }
        finally
        {
            if (_mount is not null) _mount.Content = null;
            if (_logger is IDisposable disposable) disposable.Dispose();
            _logger = null;
            _composition.Release();
            _lifetime.Dispose();
        }
    }
}
