using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.I18n;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Runtime;
using GalNet.GameHost;
using GalNet.Presentation.Abstractions.Application;
using GalNet.Primitives.Builtins;
using GalNet.Primitives.Builtins.Audio;
using GalNet.Runtime.Progress;

namespace GalNet.Avalonia.GameHost;

/// <summary>Host observation of actual built-in dialogue invocations; borrows the underlying dispatcher.</summary>
public sealed class DialogueHistoryDispatcher(IPrimitiveDispatcher inner, DialogueHistoryJournal journal,
    IGameProgressService progress, CharacterCatalog characters) : IPrimitiveDispatcher
{
    private readonly object _gate = new();
    private GameState? _state;
    private PendingText? _pending;
    private CandidateVoice? _voice;
    public Func<bool> SuppressRecording { get; set; } = () => false;
    public event Action<DialogueHistoryLine, AudioPlaybackTicket?>? TextStarting;
    public event Action? StoryContinuing;
    public StateCatalog StateCatalog => inner.StateCatalog;
    public bool RequiresStateRestore => inner.RequiresStateRestore;
    public IReadOnlyCollection<EntryDefinition> Primitives => inner.Primitives;
    public bool TryGetEntry(string primitiveType, out EntryDefinition? entry) => inner.TryGetEntry(primitiveType, out entry);
    public bool CanExecute(string primitiveType) => inner.CanExecute(primitiveType);
    public void ValidateState(IGameContext runtime, GameState state) => inner.ValidateState(runtime, state);
    public void OnInstructionSkipped(PrimitiveEntry entry, IGameContext runtime)
    {
        lock (_gate) _voice = null;
        inner.OnInstructionSkipped(entry, runtime);
    }
    public void OnPlaybackContinuing()
    {
        StoryContinuing?.Invoke();
        inner.OnPlaybackContinuing();
    }
    public void OnCheckpointCreating(IGameContext runtime)
    {
        FlushCompleted();
        inner.OnCheckpointCreating(runtime);
    }

    public PrimitiveInstance Dispatch(PrimitiveEntry entry, IGameContext runtime, CancellationToken cancellationToken,
        PrimitiveInvocationMode mode = PrimitiveInvocationMode.Normal)
    {
        lock (_gate)
        {
            runtime.State.Configure(StateCatalog);
            if (!ReferenceEquals(_state, runtime.State))
            {
                _state = runtime.State; _pending = null; _voice = null;
                journal.Rewind(_state);
            }
            FlushCompleted();
            StoryContinuing?.Invoke();
            DialogueHistoryLine? line = null;
            AudioPlaybackTicket? original = null;
            var sourceId = SourceId(entry.StableId);
            inner.TryGetEntry(entry.Type, out var entryDefinition);
            var isText = entry.Type == BuiltinEntryIds.Text && entryDefinition?.FieldDataType == typeof(DialogueTextOptions) ||
                entry.Type == BuiltinEntryIds.AppendNvl && entryDefinition?.FieldDataType == typeof(NvlAppendOptions);
            if (isText)
            {
                var arguments = entry.Arguments;
                var text = arguments.TryGetProperty("content", out var content) ? runtime.TextResolver.Resolve(content.GetString() ?? "") : "";
                var speaker = "";
                if (entry.Type == BuiltinEntryIds.Text && arguments.TryGetProperty("speaker", out var speakerValue) &&
                    speakerValue.GetString() is { } characterId && !string.IsNullOrWhiteSpace(characterId))
                    speaker = runtime.TextResolver.Resolve(characters.GetRequired(characterId).DisplayNameKey);
                var voice = _voice is { } candidate && candidate.GroupId == runtime.CurrentNodeId && candidate.SourceId == sourceId &&
                    candidate.TargetId == entry.StableId && sourceId is not null ? candidate : null;
                line = new(runtime.CurrentNodeId, entry.StableId, speaker, text, progress.IsRead(runtime.CurrentNodeId, entry.StableId), voice?.Voice);
                original = voice?.Ticket;
                _voice = null;
                TextStarting?.Invoke(line, original);
            }
            else _voice = null;

            var instance = inner.Dispatch(entry, runtime, cancellationToken, mode);
            if (entry.Type == BuiltinEntryIds.PlayAudio && sourceId is not null && entryDefinition?.FieldDataType == typeof(AudioPlayOptions))
            {
                var (_, options) = PrimitiveArgumentHelper.NormalizeFields<AudioPlayOptions>(entryDefinition.DynamicParameters, entry.Arguments);
                var primitiveIndex = int.Parse(entry.StableId.AsSpan(entry.StableId.LastIndexOf('#') + 1), System.Globalization.CultureInfo.InvariantCulture);
                var target = sourceId + "#" + (primitiveIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(options.Asset) && !string.IsNullOrWhiteSpace(options.TrackId))
                    _voice = new(runtime.CurrentNodeId, sourceId, target, new(options.Asset, options.TrackId, options.Gain),
                        (instance as AudioPlaybackPrimitiveInstance)?.Ticket);
            }
            if (line is not null && !SuppressRecording())
            {
                var pending = new PendingText(runtime.State, line, instance, mode == PrimitiveInvocationMode.CatchUp, cancellationToken);
                _pending = pending;
                FlushCompleted();
                _ = ObserveAsync(pending);
            }
            return instance;
        }
    }

    public void FlushCompleted()
    {
        lock (_gate)
        {
            if (_pending is not { } pending || pending.Token.IsCancellationRequested || !ReferenceEquals(pending.State, _state)) return;
            if (!pending.CatchUp && pending.Instance is not IAdvanceWaitInstance { IsTextPresented: true }) return;
            _pending = null;
            journal.Append(pending.State, pending.Line);
        }
    }

    private async Task ObserveAsync(PendingText pending)
    {
        try { await pending.Instance.PresentationCompletion.ConfigureAwait(false); }
        catch (Exception) { return; }
        lock (_gate) { if (ReferenceEquals(_pending, pending)) FlushCompleted(); }
    }

    private static string? SourceId(string id)
    {
        var separator = id.LastIndexOf('#');
        return separator > 0 && int.TryParse(id.AsSpan(separator + 1), out var index) && index > 0 ? id[..separator] : null;
    }

    public void Dispose() { lock (_gate) { _pending = null; _voice = null; } }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    private sealed record CandidateVoice(string GroupId, string SourceId, string TargetId, DialogueVoice Voice, AudioPlaybackTicket? Ticket);
    private sealed record PendingText(GameState State, DialogueHistoryLine Line, PrimitiveInstance Instance, bool CatchUp, CancellationToken Token);
}
