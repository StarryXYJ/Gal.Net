using Avalonia.Threading;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Debugging;
using GalNet.Runtime.Engine;
using GalNet.Avalonia.GameHost;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Avalonia.Debugging;

/// <summary>Project opt-in and policies; the reusable host does not depend on the protocol.</summary>
public sealed class AvaloniaExternalDebugSession : IAsyncDisposable
{
    private readonly GameDebugHost _host;
    private readonly IServiceProvider _services;
    private readonly GameDebugHostOptions _options;

    public AvaloniaExternalDebugSession(IServiceProvider services, GameDebugHostOptions options)
    {
        _services = services;
        _options = options;
        _host = new GameDebugHost(async token =>
        {
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(async () =>
                await services.GetRequiredService<TitlePageViewModel>().StartNewGameCommand.ExecuteAsync(null));
        });
    }
    public Task ConnectAsync(DebugInvitation invitation, CancellationToken token) => _host.ConnectAsync(invitation, token);
    public Task DisconnectAsync() => _host.DisconnectAsync();
    public IDisposable Attach(GameEngine engine)
    {
        var run = new EngineDebugRun(engine, new GameDebugOptions { Instructions = _options.Instructions },
            token => _services.GetRequiredService<AvaloniaGameSession>().StopAsync(token));
        try { return new Binding(_host.AttachRun(run), run); }
        catch { run.Dispose(); throw; }
    }
    public ValueTask DisposeAsync() => _host.DisposeAsync();
    private sealed class Binding(IDisposable binding, IDisposable run) : IDisposable
    {
        public void Dispose() { binding.Dispose(); run.Dispose(); }
    }
}
