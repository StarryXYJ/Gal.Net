using Avalonia.Threading;
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
    private EngineDebugRun? _run;
    private bool _treatAllTextAsRead;
    public bool HasRun => _run is not null;
    public event Action? RunChanged;

    public AvaloniaExternalDebugSession(IServiceProvider services, GameDebugHostOptions options)
    {
        _services = services;
        _options = options;
        _host = new GameDebugHost(async token =>
        {
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(async () =>
                await services.GetRequiredService<IPlayerApplication>().StartNewGameAsync());
        });
    }
    public Task ConnectAsync(DebugInvitation invitation, CancellationToken token) => _host.ConnectAsync(invitation, token);
    public Task DisconnectAsync() => _host.DisconnectAsync();
    public IDisposable Attach(GameEngine engine)
    {
        var run = new EngineDebugRun(engine, new GameDebugOptions { Instructions = _options.Instructions, TreatAllTextAsRead = _treatAllTextAsRead },
            token => _services.GetRequiredService<AvaloniaGameSession>().StopAsync(token));
        try
        {
            var binding = _host.AttachRun(run);
            _run = run;
            RunChanged?.Invoke();
            return new Binding(this, binding, run);
        }
        catch { run.Dispose(); throw; }
    }
    public async Task SetTreatAllTextAsReadAsync(bool enabled, CancellationToken token = default)
    {
        _treatAllTextAsRead = enabled;
        if (_run is { } run) await run.SetTreatAllTextAsReadAsync(enabled, token);
    }
    public Task SeekGroupStartAsync(CancellationToken token = default) =>
        (_run ?? throw new InvalidOperationException("No active game.")).SeekCurrentGroupStartAsync(token);
    public Task SkipGroupEndAsync(CancellationToken token = default) =>
        (_run ?? throw new InvalidOperationException("No active game.")).SkipToCurrentGroupEndAsync(token);
    public ValueTask DisposeAsync() => _host.DisposeAsync();
    private sealed class Binding(AvaloniaExternalDebugSession owner, IDisposable binding, EngineDebugRun run) : IDisposable
    {
        public void Dispose()
        {
            binding.Dispose();
            if (ReferenceEquals(owner._run, run)) { owner._run = null; owner.RunChanged?.Invoke(); }
            run.Dispose();
        }
    }
}
