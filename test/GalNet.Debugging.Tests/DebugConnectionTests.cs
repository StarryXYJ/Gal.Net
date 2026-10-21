using System.Text.Json;
using GalNet.Debugging;

namespace GalNet.Debugging.Tests;

public class DebugConnectionTests
{
    [Test]
    public async Task ConnectedAppCanAttachDetachAndReplaceRunsWithoutReconnecting()
    {
        using var listener = new DebugListener(new("project", "content"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = listener.AcceptAsync(timeout.Token);
        await using var host = new GameDebugHost();
        await host.ConnectAsync(listener.Invitation, timeout.Token);
        await using var peer = await accepted;
        Assert.That((await peer.RequestAsync("status", cancellationToken: timeout.Token)).GetProperty("run").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var first = new Run("first");
        var binding = host.AttachRun(first);
        Assert.That((await peer.RequestAsync("status", cancellationToken: timeout.Token)).GetProperty("run").GetProperty("runId").GetString(), Is.EqualTo("first"));
        binding.Dispose();
        using var next = host.AttachRun(new Run("second"));
        binding.Dispose();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await peer.RequestAsync("read", Command("first"), timeout.Token));
        var result = await peer.RequestAsync("read", Command("second"), timeout.Token);
        Assert.That(result.GetString(), Is.EqualTo("second"));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task StartCallbackIsOptional(bool enabled)
    {
        var starts = 0;
        using var listener = new DebugListener(new("project", "content"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = listener.AcceptAsync(timeout.Token);
        await using var host = new GameDebugHost(enabled ? _ => { starts++; return Task.CompletedTask; } : null);
        await host.ConnectAsync(listener.Invitation, timeout.Token);
        await using var peer = await accepted;
        Assert.That((await peer.RequestAsync("status", cancellationToken: timeout.Token)).GetProperty("canStart").GetBoolean(), Is.EqualTo(enabled));
        if (enabled) await peer.RequestAsync("start", cancellationToken: timeout.Token);
        else Assert.ThrowsAsync<InvalidOperationException>(async () => await peer.RequestAsync("start", cancellationToken: timeout.Token));
        Assert.That(starts, Is.EqualTo(enabled ? 1 : 0));
    }
    [Test]
    public async Task IncorrectProjectIsRejectedAndDoesNotConsumeTheListener()
    {
        using var listener = new DebugListener(new("project", "content"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var accepted = listener.AcceptAsync(timeout.Token);
        await using var wrong = new GameDebugHost();
        Assert.ThrowsAsync<EndOfStreamException>(async () => await wrong.ConnectAsync(listener.Invitation with { Identity = new("other", "content") }, timeout.Token));
        Assert.That(accepted.IsCompleted, Is.False);
        await using var correct = new GameDebugHost();
        await correct.ConnectAsync(listener.Invitation, timeout.Token);
        await using var peer = await accepted;
        Assert.That(peer.IsConnected, Is.True);
    }
    private static DebugCommand Command(string run) => new("read", run, 1, JsonSerializer.SerializeToElement(new { }));
    private sealed class Run(string id) : IDebugGameRun
    {
        public DebugRunInfo Info => new(id, 1, ["read"]);
        public Task<object?> ExecuteAsync(DebugCommand command, CancellationToken cancellationToken) => Task.FromResult<object?>(id);
    }
}
