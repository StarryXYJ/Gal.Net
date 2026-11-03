using System.Text.Json;
using GalNet.Runtime.Engine;

namespace GalNet.Debugging;

public sealed record DebugVariableEdit(GameDebugVariable Expected, string Value);
public sealed record DebugChoiceSelection(string ChoiceId, int Index);

/// <summary>Default adapter; the host retains ownership of Engine and presentation services.</summary>
public sealed class EngineDebugRun : IDebugGameRun, IDisposable
{
    private readonly GameEngine _engine;
    private readonly GameEngine.DebugSession _session;
    private readonly Func<CancellationToken, Task>? _stop;
    private readonly IDebugContentRevisionController? _content;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private bool _disposed;
    private long _operationGeneration;
    private long? OperationGeneration => Interlocked.Read(ref _operationGeneration) is var value && value != 0 ? value : null;
    public EngineDebugRun(GameEngine engine, GameDebugOptions? options = null, Func<CancellationToken, Task>? stop = null,
        IDebugContentRevisionController? content = null)
    {
        _engine = engine;
        _session = engine.AttachDebugging(options);
        _stop = stop;
        _content = content;
    }
    public DebugRunInfo Info => new(_id, _session.Generation,
        _content is null
            ? ["inspect", "pause", "resume", "step", "breakpoints", "setVariable", "choose", "seek", "groupEnd", "stop", "cancel"]
            : ["inspect", "pause", "resume", "step", "breakpoints", "setVariable", "choose", "seek", "groupEnd", "stop", "cancel",
                "content.install", "content.activate", "content.activateAndSeekGroupStart"], OperationGeneration);
    public Task SetTreatAllTextAsReadAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _session.SetTreatAllTextAsReadAsync(enabled, cancellationToken);

    public async Task SeekCurrentGroupStartAsync(CancellationToken cancellationToken = default)
    {
        var state = await _session.InspectAsync(cancellationToken).ConfigureAwait(false);
        await _session.SeekGroupStartAsync(state.Generation, state.NodeId, cancellationToken).ConfigureAwait(false);
        await _session.ResumeAsync(_session.Generation, cancellationToken).ConfigureAwait(false);
    }

    public Task SkipToCurrentGroupEndAsync(CancellationToken cancellationToken = default) =>
        _engine.SkipToGroupEndAsync(cancellationToken: cancellationToken);
    public async Task<object?> ExecuteAsync(DebugCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (command.RunId != _id || (command.Generation != _session.Generation && !(command.Method is "cancel" or "stop" && command.Generation == OperationGeneration)))
            throw new InvalidOperationException("The debug execution has changed.");
        var generation = command.Generation ?? throw new InvalidDataException("Missing execution generation.");
        switch (command.Method)
        {
            case "inspect":
                var inspected = await _session.InspectAsync(cancellationToken).ConfigureAwait(false);
                return _content is null ? inspected : inspected with
                {
                    LatestRevision = _content.Versions.LatestRevision,
                    RunRevision = _content.Versions.RunRevision
                };
            case "content.install":
                return await RequireContent().InstallAsync(Read<DebugContentInstallRequest>(command.Arguments), cancellationToken).ConfigureAwait(false);
            case "content.activate":
                return await ActivateContentAsync(generation, Read<DebugContentActivateRequest>(command.Arguments), false, cancellationToken).ConfigureAwait(false);
            case "content.activateAndSeekGroupStart":
                return await ActivateContentAsync(generation, Read<DebugContentActivateRequest>(command.Arguments), true, cancellationToken).ConfigureAwait(false);
            case "pause": await _session.PauseAsync(generation, cancellationToken).ConfigureAwait(false); break;
            case "resume": await _session.ResumeAsync(generation, cancellationToken).ConfigureAwait(false); break;
            case "step": await _session.StepAsync(generation, Read<GameDebugSpan>(command.Arguments), cancellationToken).ConfigureAwait(false); break;
            case "breakpoints": await _session.SetBreakpointsAsync(generation, Read<GameDebugSpan[]>(command.Arguments), cancellationToken).ConfigureAwait(false); break;
            case "setVariable":
                var edit = Read<DebugVariableEdit>(command.Arguments);
                await _session.SetVariableAsync(generation, edit.Expected, edit.Value, cancellationToken).ConfigureAwait(false); break;
            case "choose":
                var choice = Read<DebugChoiceSelection>(command.Arguments);
                await _session.ChooseAsync(generation, choice.ChoiceId, choice.Index, cancellationToken).ConfigureAwait(false); break;
            case "seek":
                Interlocked.Exchange(ref _operationGeneration, generation);
                try { await _session.SeekAsync(generation, Read<GameDebugSeek>(command.Arguments), cancellationToken).ConfigureAwait(false); }
                finally { Interlocked.Exchange(ref _operationGeneration, 0); }
                break;
            case "groupEnd":
                var state = await _session.InspectAsync(cancellationToken).ConfigureAwait(false);
                var group = state.Groups.SingleOrDefault(group => group.Id == state.NodeId) ?? throw new InvalidOperationException("No current Group.");
                Interlocked.Exchange(ref _operationGeneration, generation);
                try { await _session.SeekAsync(generation, new(group.Id, group.Entries.Length, []), cancellationToken).ConfigureAwait(false); }
                finally { Interlocked.Exchange(ref _operationGeneration, 0); }
                break;
            case "cancel": _session.CancelSeek(); _engine.CancelCatchUp(); break;
            case "stop":
                _session.CancelSeek();
                if (_stop is not null) await _stop(cancellationToken).ConfigureAwait(false);
                else await _engine.StopAsync().ConfigureAwait(false);
                break;
            default: throw new NotSupportedException($"Unknown debug command '{command.Method}'.");
        }
        return null;
    }
    private async Task<DebugContentActivationResult> ActivateContentAsync(long generation, DebugContentActivateRequest request,
        bool seekGroupStart, CancellationToken cancellationToken)
    {
        var content = RequireContent();
        var graph = content.GetLatestGraph(request.Revision);
        var next = await _session.ReplaceContentAsync(generation,
            new(graph, request.Expected, request.CandidateEntryIndex), cancellationToken).ConfigureAwait(false);
        content.CommitActivation(request.Revision);
        if (seekGroupStart)
        {
            await _session.SeekGroupStartAsync(next, request.Expected.NodeId, cancellationToken).ConfigureAwait(false);
            await _session.ResumeAsync(_session.Generation, cancellationToken).ConfigureAwait(false);
            next = _session.Generation;
        }
        return new(next, content.Versions);
    }
    private IDebugContentRevisionController RequireContent() =>
        _content ?? throw new NotSupportedException("This debug App does not support content revisions.");
    private static T Read<T>(JsonElement value) => value.Deserialize<T>(DebugWire.Json) ?? throw new InvalidDataException("Missing debug arguments.");
    public async Task OnDisconnectedAsync()
    {
        if (_disposed) return;
        try
        {
            var generation = _session.Generation;
            await _session.SetBreakpointsAsync(generation, []).ConfigureAwait(false);
            await _session.ResumeAsync(generation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _session.Dispose(); }
}
