using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Audio;
using GalNet.Core.Primitives;
using GalNet.Core.Settings;
using GalNet.Presentation.Abstractions.Audio;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Runtime.Audio;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinAudioModule : PrimitiveModule
{
    private readonly AudioRuntime _runtime;
    private readonly AudioScope _scope;
    public void ApplyPlayerSettings(GameSettings settings) => _runtime.ApplyPlayerSettings(settings);
    public void BeginCatchUp() => _runtime.BeginCatchUp();
    public void EndCatchUp() => _runtime.EndCatchUp();

    public BuiltinAudioModule(IAudioPlaybackHost? host = null) : this(new AudioRuntime(host)) { }

    private BuiltinAudioModule(AudioRuntime runtime) : this(runtime, new AudioScope(runtime)) { }

    private BuiltinAudioModule(AudioRuntime runtime, AudioScope scope) : base(CreatePrimitives(runtime, scope))
    {
        _runtime = runtime;
        _scope = scope;
        _runtime.Diagnostic += OnDiagnostic;
    }

    public override void Dispose()
    {
        _scope.Dispose();
        _runtime.Dispose();
        _runtime.Diagnostic -= OnDiagnostic;
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        try { await _runtime.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            _runtime.Diagnostic -= OnDiagnostic;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static List<AudioTrackDefinition> Tracks(PrimitiveCreateContext context) => context.Runtime.Settings.Get<ProjectSettings>().AudioTracks;

    private static IReadOnlyList<PrimitiveImplementation> CreatePrimitives(AudioRuntime runtime, AudioScope scope) =>
    [
        BuiltinInstructionFactory.Create<PlayAudioEntry>(context => Playback(context, runtime, scope,
            Arguments.Enum(context, "placement", AudioPlacement.Enqueue))),
        BuiltinInstructionFactory.Create<StopAudioEntry>(context => Audio(context, scope, () =>
            runtime.Stop(Tracks(context), new AudioStopRequest(
                Arguments.String(context, "trackId"), Arguments.Bool(context, "clearQueue", true),
                Arguments.Float(context, "fadeOut"))))),
        BuiltinInstructionFactory.Create<PauseAudioEntry>(context => Audio(context, scope, () =>
            runtime.Pause(Tracks(context), Arguments.String(context, "trackId")))),
        BuiltinInstructionFactory.Create<ResumeAudioEntry>(context => Audio(context, scope, () =>
            runtime.Resume(Tracks(context), Arguments.String(context, "trackId")))),
        BuiltinInstructionFactory.Create<NextAudioEntry>(context => Audio(context, scope, () =>
            runtime.Next(Tracks(context), Arguments.String(context, "trackId")))),
        BuiltinInstructionFactory.Create<ClearAudioEntry>(context => Audio(context, scope, () =>
            runtime.Clear(Tracks(context), Arguments.String(context, "trackId")))),
        BuiltinInstructionFactory.Create<EnqueueAudioEntry>(context => Playback(context, runtime, scope, AudioPlacement.Enqueue)),
        BuiltinInstructionFactory.Create<ApplyAudioSEffectEntry>(context => Audio(context, scope, () =>
            runtime.ApplySEffect(Tracks(context), new AudioSEffectRequest(
                Arguments.String(context, "trackId"), Arguments.String(context, "instanceId"),
                Arguments.String(context, "id"), Arguments.Json<JsonElement>(context, "parameters"))))),
        BuiltinInstructionFactory.Create<StopAudioSEffectEntry>(context => Audio(context, scope, () =>
            runtime.StopSEffect(Tracks(context), Arguments.String(context, "trackId"), Arguments.String(context, "instanceId"))))
    ];

    private static AudioPlayRequest PlayRequest(PrimitiveCreateContext context, AudioPlacement placement) => new(
        Arguments.String(context, "trackId"), Arguments.String(context, "asset"), placement,
        Arguments.Float(context, "gain", 1), Arguments.Float(context, "fadeIn"));

    private static AudioPlaybackPrimitiveInstance Playback(PrimitiveCreateContext context, AudioRuntime runtime,
        AudioScope scope, AudioPlacement placement) => new(() =>
        {
            scope.Bind(context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            runtime.ApplyPlayerSettings(context.Runtime.Settings.Get<GameSettings>());
            return runtime.Play(Tracks(context), PlayRequest(context, placement));
        }, Arguments.Bool(context, "autoBlocking"), context.BatchId, context.ScopeCancellation);

    private static void OnDiagnostic(string message) => GameLog.Logger.Warning("Audio: {Diagnostic}", message);

    private sealed class AudioScope(AudioRuntime runtime) : IDisposable
    {
        private CancellationToken _token;
        private CancellationTokenRegistration _registration;
        public void Bind(CancellationToken token)
        {
            if (_token == token) return;
            _registration.Dispose();
            _token = token;
            _registration = token.Register(runtime.Reset);
        }
        public void Dispose() => _registration.Dispose();
    }

    private static ImmediatePrimitiveInstance Audio(PrimitiveCreateContext context, AudioScope scope, Action action) =>
        new(() =>
        {
            scope.Bind(context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            action();
        }, batchId: context.BatchId);
}
