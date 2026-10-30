using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Runtime.Logging;
using GalNet.Sample.CrossPlatform.Views;
using GalNet.Sample.Player;
using GalNet.Sample.Player.Presentation;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;

namespace GalNet.Sample.CrossPlatform;

public partial class App : Application, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _composition = new(1, 1);
    private SamplePlayerSession? _player;
    private ContentControl? _mount;
    private Window? _window;
    private Task? _startup;
    private bool _stopping;
    private Serilog.ILogger? _logger;
    private int _generation;
    private Task? _shutdown;

    public static Func<CancellationToken, Task<PlayerLaunchPaths>> PrepareLaunchAsync { get; set; } = _ =>
    {
        var content = ApplicationPackageLayout.GetContentDirectory(ApplicationPackageLayout.FindApplicationRoot(AppContext.BaseDirectory));
        return Task.FromResult(new PlayerLaunchPaths(content, SamplePlayerPaths.GetProfileDirectory(content)));
    };

    public Action? ExitRequested { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { Content = CreateHostView() };
            _window = desktop.MainWindow = window;
            window.Resources.MergedDictionaries.Add(SamplePlayerServices.PlayerPalette.CreateResources());
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
        var host = new ContentControl { Content = _player?.View };
        _mount = host;
        host.AttachedToVisualTree += async (_, _) =>
        {
            _startup ??= StartAsync();
            await _startup;
        };
        return host;
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
                    _logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
                        .WriteTo.File(Path.Combine(paths.ProfileDirectory, "logs/game-.log"), rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 14, shared: true, formatProvider: CultureInfo.InvariantCulture).CreateLogger();
                    Log.Logger = _logger;
                }
                var options = SamplePlayerServices.CreateHostOptions(screenshotDirectory: title =>
                    paths.ScreenshotDirectory ?? SampleScreenshotOutput.GetHostDirectory(title));
                _player = await SamplePlayerSession.CreateAsync(options);
                if (_window is not null) _player.BindWindow(_window);
                _player.Services.GetRequiredService<TitlePageViewModel>().ExitRequested = () =>
                {
                    if (_window is not null) _window.Close();
                    else ExitRequested?.Invoke();
                };
                if (_mount is not null) _mount.Content = _player.View;
                initialization = _player.InitializeAsync(paths.ContentDirectory, paths.ProfileDirectory);
            }
            finally { _composition.Release(); }
            // Shutdown must be able to cancel onboarding while initialization awaits user input.
            await initialization;
        }
        catch (OperationCanceledException) when (_stopping || generation != _generation) { }
        catch (Exception error)
        {
            GameLog.Logger.Error(error, "Cross-platform player startup failed");
            if (!_stopping && generation == _generation && _mount is not null)
                _mount.Content = new TextBlock { Text = error.Message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(24) };
        }
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
                await _player.DisposeAsync();
                _player = null;
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
            _composition.Dispose();
            _lifetime.Dispose();
        }
    }
}
