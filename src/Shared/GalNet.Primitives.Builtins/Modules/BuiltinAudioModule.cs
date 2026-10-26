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

    public BuiltinAudioModule(IAudioPlaybackHost? host = null, ISEffectCatalog? effects = null) : this(new AudioRuntime(host, effects)) { }

    private BuiltinAudioModule(AudioRuntime runtime) : this(runtime, new AudioScope(runtime)) { }

    private BuiltinAudioModule(AudioRuntime runtime, AudioScope scope) : base(registry => RegisterPrimitives(registry, runtime, scope))
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

    private static void RegisterPrimitives(PrimitiveRegistry registry, AudioRuntime runtime, AudioScope scope)
    {
        registry.State<AudioTrackState>("builtin.audio.tracks", value => value.Id, validate: AudioRuntime.ValidateTrack);
        registry.State<AudioSEffectState>("builtin.audio.seffects", value => AudioSEffectState.Key(value.TrackId, value.InstanceId));
        registry.ValidateState((game, state) => runtime.Validate(state, game.Settings.Get<ProjectSettings>().AudioTracks));
        registry.Restore("builtin.audio", async context =>
        {
            scope.Bind(context.Runtime, context.CancellationToken);
            context.Own(() => runtime.SuspendAsync(context.Runtime.State));
            runtime.ApplyPlayerSettings(context.Runtime.Settings.Get<GameSettings>());
            await runtime.RestoreAsync(context.Runtime.State, context.Runtime.Settings.Get<ProjectSettings>().AudioTracks,
                context.CancellationToken).ConfigureAwait(false);
        });
        registry.Register<AudioPlayOptions>(BuiltinEntryIds.PlayAudio, (context, options) => Playback(context, runtime, scope,
            options, options.Placement ?? AudioPlacement.Enqueue));
        registry.Register<AudioStopOptions>(BuiltinEntryIds.StopAudio, (context, options) => Audio(context, scope, () =>
            runtime.Stop(Tracks(context), new AudioStopRequest(
                options.TrackId ?? "", options.ClearQueue ?? true, options.FadeOut))));
        Track(registry, BuiltinEntryIds.PauseAudio, scope, (context, track) => runtime.Pause(Tracks(context), track));
        Track(registry, BuiltinEntryIds.ResumeAudio, scope, (context, track) => runtime.Resume(Tracks(context), track));
        Track(registry, BuiltinEntryIds.NextAudio, scope, (context, track) => runtime.Next(Tracks(context), track));
        Track(registry, BuiltinEntryIds.ClearAudio, scope, (context, track) => runtime.Clear(Tracks(context), track));
        registry.Register<AudioPlayOptions>(BuiltinEntryIds.EnqueueAudio, (context, options) => Playback(context, runtime, scope, options, AudioPlacement.Enqueue));
        registry.Register<AudioSEffectOptions>(BuiltinEntryIds.ApplyAudioSEffect, (context, options) => Audio(context, scope, () =>
            runtime.ApplySEffect(Tracks(context), new AudioSEffectRequest(
                options.TrackId ?? "", options.InstanceId ?? "",
                options.Id ?? "", Arguments.Json<JsonElement>(options.Parameters)))));
        registry.Register<AudioSEffectStopOptions>(BuiltinEntryIds.StopAudioSEffect, (context, options) => Audio(context, scope, () =>
            runtime.StopSEffect(Tracks(context), options.TrackId ?? "", options.InstanceId ?? "")));
    }

    private static AudioPlayRequest PlayRequest(AudioPlayOptions options, AudioPlacement placement) => new(
        options.TrackId ?? "", options.Asset ?? "", placement, options.Gain, options.FadeIn);

    private static AudioPlaybackPrimitiveInstance Playback(PrimitiveCreateContext context, AudioRuntime runtime,
        AudioScope scope, AudioPlayOptions options, AudioPlacement placement) => new(() =>
        {
            scope.Bind(context.Runtime, context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            runtime.ApplyPlayerSettings(context.Runtime.Settings.Get<GameSettings>());
            return runtime.Play(Tracks(context), PlayRequest(options, placement));
        }, options.AutoBlocking ?? false, context.BatchId, context.ScopeCancellation);

    private static void OnDiagnostic(string message) => GameLog.Logger.Warning("Audio: {Diagnostic}", message);

    private sealed class AudioScope(AudioRuntime runtime) : IDisposable
    {
        private CancellationToken _token;
        private GalNet.Core.Runtime.GameState? _boundState;
        private CancellationTokenRegistration _registration;
        public void Bind(GalNet.Core.Runtime.IGameContext game, CancellationToken token)
        {
            if (_token == token && ReferenceEquals(_boundState, game.State)) return;
            _boundState = game.State;
            _registration.Dispose();
            _token = token;
            runtime.Bind(game.State);
            var state = game.State;
            _registration = token.Register(() => runtime.Reset(state));
        }
        public void Dispose() => _registration.Dispose();
    }

    private static void Track(PrimitiveRegistry registry, string type, AudioScope scope, Action<PrimitiveCreateContext, string> action) =>
        registry.Immediate<AudioTrackOptions>(type, (context, options) =>
        {
            scope.Bind(context.Runtime, context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            action(context, options.TrackId ?? "");
        });

    private static ImmediatePrimitiveInstance Audio(PrimitiveCreateContext context, AudioScope scope, Action action) =>
        new(() =>
        {
            scope.Bind(context.Runtime, context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            action();
        }, batchId: context.BatchId);
}
