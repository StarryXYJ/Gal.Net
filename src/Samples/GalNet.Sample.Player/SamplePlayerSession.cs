using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Player;

/// <summary>Temporary Sample configuration adapter for the reusable host.</summary>
public sealed class SamplePlayerSession : IAsyncDisposable
{
    private readonly AvaloniaPlayerHost _host;

    private SamplePlayerSession(AvaloniaPlayerHost host) => _host = host;

    public static async Task<SamplePlayerSession> CreateAsync(AvaloniaGameHostOptions options,
        Action<IServiceCollection>? configure = null) =>
        new(await AvaloniaPlayerHost.CreateAsync(options, services =>
        {
            SamplePlayerServices.ConfigureServices(services);
            services.AddScoped(_ => SamplePlayerServices.CreateView());
            configure?.Invoke(services);
        }));

    public IServiceProvider Services => _host.Services;
    public GalNet.Avalonia.GameHost.Views.PlayerView View => _host.View;
    public AvaloniaGameSession Session => _host.Session;
    public IAvaloniaPlayerSettings Settings => _host.Settings;
    public Task SuspendAsync() => _host.SuspendAsync();
    public Task<bool> HandleBackAsync() => _host.HandleBackAsync();
    public void BindWindow(Window? window) => _host.BindWindow(window);
    public Task<bool> InitializeAsync(string? contentDirectory, string? profileDirectory) =>
        _host.InitializeAsync(contentDirectory, profileDirectory);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _host.DisposeAsync();
    }
}
