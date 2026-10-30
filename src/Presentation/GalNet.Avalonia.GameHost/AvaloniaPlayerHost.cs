using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.GameHost;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.GameHost.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GalNet.Avalonia.GameHost;

/// <summary>One UI-thread-owned player composition. A platform can replace it without restarting its process.</summary>
public sealed class AvaloniaPlayerHost : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;
    private readonly CancellationTokenSource _startup = new();
    private Task<bool>? _initialization;
    private Task? _disposal;
    private Func<string, object>? _previousResolver;
    private Func<string, object>? _resolver;

    private AvaloniaPlayerHost(AvaloniaGameHostOptions options, Action<IServiceCollection> configure)
    {
        Dispatcher.UIThread.VerifyAccess();
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => options)
            .AddScoped<AvaloniaGameSession>()
            .AddScoped<IGameSessionService>(services => services.GetRequiredService<AvaloniaGameSession>());
        registrations.TryAddScoped<LanguageSelectionViewModel>();
        registrations.TryAddScoped<PlayerView>();
        configure(registrations);
        _root = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _root.CreateAsyncScope();
    }

    /// <summary>The caller registers project pages, localization, settings and platform services.</summary>
    public static async Task<AvaloniaPlayerHost> CreateAsync(AvaloniaGameHostOptions options,
        Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        var result = new AvaloniaPlayerHost(options, configure);
        try
        {
            result.Settings = result.Services.GetRequiredService<IAvaloniaPlayerSettings>();
            var localization = result.Services.GetRequiredService<IPlayerLocalization>();
            result._previousResolver = GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve;
            result._resolver = localization.GetString;
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = result._resolver;
            result.Session = result.Services.GetRequiredService<AvaloniaGameSession>();
            result.Services.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>(NavigationTransition.None);
            result.View = result.Services.GetRequiredService<PlayerView>();
            result.View.Attach(result.Services.GetRequiredService<GameShell>(), result.Services.GetRequiredService<LanguageSelectionViewModel>());
            return result;
        }
        catch (Exception error)
        {
            var cleanup = new ResourceCleanup();
            cleanup.Run(result.RestoreResolver);
            if (result.View is not null) cleanup.Run(result.View.Detach);
            await cleanup.RunAsync(() => result._scope.DisposeAsync());
            await cleanup.RunAsync(() => result._root.DisposeAsync());
            cleanup.Run(result._startup.Dispose);
            cleanup.ThrowIfFailed(error);
            throw;
        }
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public PlayerView View { get; private set; } = null!;
    public AvaloniaGameSession Session { get; private set; } = null!;
    public IAvaloniaPlayerSettings Settings { get; private set; } = null!;

    public async Task SuspendAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        var cleanup = new ResourceCleanup();
        cleanup.Run(() => Services.GetRequiredService<GalleryMediaService>().StopPlayback());
        await cleanup.RunAsync(() => new(Session.SuspendAsync()));
        if (Session.IsReady)
            cleanup.Run(() => Services.GetRequiredService<IGameNavigationService>().ResetTo<TitlePageViewModel>(NavigationTransition.None));
        cleanup.ThrowIfFailed();
    }

    public async Task<bool> HandleBackAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (View.CancelExitConfirmation()) return false;
        var navigation = Services.GetRequiredService<IGameNavigationService>();
        if (navigation.CurrentViewModel is BackNavigablePageViewModel page && navigation.CanGoBack)
            page.BackCommand.Execute(null);
        else if (navigation.CurrentViewModel is GamePageViewModel gameplay)
            gameplay.OpenSettingsCommand.Execute(null);
        else if (!Services.GetRequiredService<TitlePageViewModel>().CanExit)
            return true;
        else return await View.ConfirmExitAsync();
        return false;
    }

    public void BindWindow(Window? window)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        Settings.BindWindow(window);
    }

    public Task<bool> InitializeAsync(string? contentDirectory, string? profileDirectory)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        return _initialization ??= InitializeCoreAsync(contentDirectory, profileDirectory);
    }

    private async Task<bool> InitializeCoreAsync(string? contentDirectory, string? profileDirectory)
    {
        var ready = await Session.InitializeAsync(contentDirectory, profileDirectory, View.SelectLanguageAsync, _startup.Token);
        if (!_startup.IsCancellationRequested) View.CompleteStartup(ready ? null : Session.StatusMessage);
        return ready;
    }

    public ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        GC.SuppressFinalize(this);
        return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        _startup.Cancel();
        var cleanup = new ResourceCleanup();
        if (_initialization is not null)
        {
            await cleanup.RunAsync(async () =>
            {
                try { await _initialization; }
                catch (OperationCanceledException) when (_startup.IsCancellationRequested) { }
            });
        }
        await cleanup.RunAsync(() => new(Session.StopAsync()));
        cleanup.Run(() => Settings.BindWindow(null));
        cleanup.Run(View.Detach);
        cleanup.Run(RestoreResolver);
        await cleanup.RunAsync(() => _scope.DisposeAsync());
        await cleanup.RunAsync(() => _root.DisposeAsync());
        cleanup.Run(_startup.Dispose);
        cleanup.ThrowIfFailed();
    }

    private void RestoreResolver()
    {
        if (_resolver is not null && GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve == _resolver)
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = _previousResolver;
    }
}
