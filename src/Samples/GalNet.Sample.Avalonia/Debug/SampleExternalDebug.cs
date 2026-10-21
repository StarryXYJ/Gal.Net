using Avalonia.Threading;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins;
using GalNet.Debugging;
using GalNet.Runtime.Engine;
using GalNet.Sample.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.Avalonia.Debug;

/// <summary>Project opt-in and policies; the reusable host does not depend on the protocol.</summary>
internal sealed class SampleExternalDebug : IAsyncDisposable
{
    private readonly GameDebugHost _host;
    private readonly IServiceProvider _services;
    public SampleExternalDebug(IServiceProvider services)
    {
        _services = services;
        _host = new GameDebugHost(async token =>
        {
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(async () =>
                await services.GetRequiredService<TitlePageViewModel>().StartNewGameCommand.ExecuteAsync(null));
        });
    }
    public Task ConnectAsync(DebugInvitation invitation, CancellationToken token) => _host.ConnectAsync(invitation, token);
    public IDisposable Attach(GameEngine engine)
    {
        var instructions = BuiltinSchemas.CreateAllModules().SelectMany(module => module.PrimitiveEntries.Values)
            .ToDictionary(entry => entry.Name, _ => DebugReplayBehavior.Scene, StringComparer.Ordinal);
        instructions[BuiltinEntryIds.SetVariable] = DebugReplayBehavior.Ignore;
        instructions[BuiltinEntryIds.UnlockGallery] = DebugReplayBehavior.Ignore;
        instructions["sample.screenshot"] = DebugReplayBehavior.Ignore;
        var run = new EngineDebugRun(engine, new GameDebugOptions { Instructions = instructions },
            token => _services.GetRequiredService<SampleGameSessionService>().StopAsync(token));
        try { return new Binding(_host.AttachRun(run), run); }
        catch { run.Dispose(); throw; }
    }
    public ValueTask DisposeAsync() => _host.DisposeAsync();
    private sealed class Binding(IDisposable binding, IDisposable run) : IDisposable
    {
        public void Dispose() { binding.Dispose(); run.Dispose(); }
    }
}
