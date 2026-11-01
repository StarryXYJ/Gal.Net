using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Services;
using GalNet.GameHost;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Avalonia.GameHost.ViewModels;
using GalNet.Avalonia.GameHost.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GalNet.Media.LibVlc;

namespace GalNet.Avalonia.GameHost;

/// <summary>One UI-thread-owned player composition. A platform can replace it without restarting its process.</summary>
public sealed class AvaloniaPlayerHost : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;
    private readonly CancellationTokenSource _startup = new();
    private Task<bool>? _initialization;
    private Task? _disposal;
    private IPlayerApplication? _application;
    private Func<string, object>? _previousResolver;
    private Func<string, object>? _resolver;

    private AvaloniaPlayerHost(AvaloniaGameHostOptions options, Action<IServiceCollection> configure)
    {
        Dispatcher.UIThread.VerifyAccess();
        var registrations = new ServiceCollection();
        registrations.AddScoped(_ => options)
            .AddScoped<AvaloniaGameSession>()
            .AddScoped<IGameSessionService>(services => services.GetRequiredService<AvaloniaGameSession>());
        configure(registrations);
        _root = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _scope = _root.CreateAsyncScope();
    }

    /// <summary>The caller registers project pages, localization, settings and platform services.</summary>
    public static async Task<AvaloniaPlayerHost> CreateAsync(AvaloniaGameHostOptions options,
        Action<IServiceCollection> configure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new AvaloniaPlayerHost(options, configure);
        try
        {
            // Resolve only the native media dependency here. Controls and application services stay on the UI thread.
            // Await the actual factory completion, even after cancellation, before disposing its owning scope.
            await Task.Run(() => result.Services.GetRequiredService<LibVlcRuntime>(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            result.Settings = result.Services.GetRequiredService<IAvaloniaPlayerSettings>();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken);
            var localization = result.Services.GetRequiredService<IPlayerLocalization>();
            result._previousResolver = GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve;
            result._resolver = localization.GetString;
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = result._resolver;
            result.Session = result.Services.GetRequiredService<AvaloniaGameSession>();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken);
            result._application = result.Services.GetRequiredService<IPlayerApplication>();
            result.View = result._application.View;
            result._application.Attach();
            result._application.ShowTitle();
            return result;
        }
        catch (Exception error)
        {
            var cleanup = new ResourceCleanup();
            cleanup.Run(result.RestoreResolver);
            if (result._application is not null) cleanup.Run(result._application.Detach);
            await cleanup.RunAsync(() => result._scope.DisposeAsync());
            await cleanup.RunAsync(() => result._root.DisposeAsync());
            cleanup.Run(result._startup.Dispose);
            cleanup.ThrowIfFailed(error);
            throw;
        }
    }

    public IServiceProvider Services => _scope.ServiceProvider;
    public Control View { get; private set; } = null!;
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
            cleanup.Run(_application!.ShowTitle);
        cleanup.ThrowIfFailed();
    }

    public async Task<bool> HandleBackAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        return await _application!.HandleBackAsync();
    }

    public Task<bool> ConfirmExitAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        return _application!.ConfirmExitAsync();
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
        var ready = await Session.InitializeAsync(contentDirectory, profileDirectory, _application!.SelectLanguageAsync, _startup.Token);
        if (!_startup.IsCancellationRequested) _application!.CompleteStartup(ready ? null : Session.StatusMessage);
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
        cleanup.Run(_application!.Detach);
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
