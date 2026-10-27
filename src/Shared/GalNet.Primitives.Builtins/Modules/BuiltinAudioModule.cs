using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Primitives.Builtins.Audio;
using GalNet.Runtime.Logging;

namespace GalNet.Primitives.Builtins;

public sealed class BuiltinAudioModule : PrimitiveModule
{
    private readonly AudioRuntime _runtime;
    private readonly AudioScope _scope;
    public void ApplyMix(AudioMixOptions options) => _runtime.ApplyMix(options);
    public void BeginCatchUp() => _runtime.BeginCatchUp();
    public void EndCatchUp() => _runtime.EndCatchUp();

    public BuiltinAudioModule(IAudioPlaybackHost? host = null, ISEffectCatalog? effects = null,
        AudioConfiguration? configuration = null) : this(new AudioRuntime(host, effects), configuration ?? AudioConfiguration.Empty) { }

    private BuiltinAudioModule(AudioRuntime runtime, AudioConfiguration configuration) : this(runtime, new AudioScope(runtime), configuration) { }

    private BuiltinAudioModule(AudioRuntime runtime, AudioScope scope, AudioConfiguration configuration)
        : base(registry => RegisterPrimitives(registry, runtime, scope, configuration))
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

    private static void RegisterPrimitives(PrimitiveRegistry registry, AudioRuntime runtime, AudioScope scope,
        AudioConfiguration tracks)
    {
        registry.State<AudioTrackState>(BuiltinJson.StateTypeInfo<AudioTrackState>(), "builtin.audio.tracks", value => value.Id, validate: AudioRuntime.ValidateTrack, version: 2);
        registry.State<AudioSEffectState>(BuiltinJson.StateTypeInfo<AudioSEffectState>(), "builtin.audio.seffects", value => value.Key(), version: 2);
        registry.State<AudioChannelState>(BuiltinJson.StateTypeInfo<AudioChannelState>(), "builtin.audio.channels", value => value.Key(), validate: AudioRuntime.ValidateChannel);
        registry.ValidateState((_, state) => runtime.Validate(state, tracks));
        registry.Restore("builtin.audio", async context =>
        {
            scope.Bind(context.Runtime, context.CancellationToken);
            context.Own(() => runtime.SuspendAsync(context.Runtime.State));
            await runtime.RestoreAsync(context.Runtime.State, tracks,
                context.CancellationToken).ConfigureAwait(false);
        });
        registry.Register<AudioPlayOptions>(BuiltinEntryIds.PlayAudio, (context, options) => Playback(context, runtime, scope, tracks,
            options, options.Placement ?? AudioPlacement.Enqueue));
        registry.Register<AudioStopOptions>(BuiltinEntryIds.StopAudio, (context, options) => Audio(context, scope, () =>
            runtime.Stop(tracks, new AudioStopRequest(
                options.TrackId, options.ClearQueue ?? true, options.FadeOut))));
        Track(registry, BuiltinEntryIds.PauseAudio, scope, (_, track) => runtime.Pause(tracks, track));
        Track(registry, BuiltinEntryIds.ResumeAudio, scope, (_, track) => runtime.Resume(tracks, track));
        Track(registry, BuiltinEntryIds.NextAudio, scope, (_, track) => runtime.Next(tracks, track));
        Track(registry, BuiltinEntryIds.ClearAudio, scope, (_, track) => runtime.Clear(tracks, track));
        registry.Register<AudioPlayOptions>(BuiltinEntryIds.EnqueueAudio, (context, options) => Playback(context, runtime, scope, tracks, options, AudioPlacement.Enqueue));
        registry.Register<AudioSEffectOptions>(BuiltinEntryIds.ApplyAudioSEffect, (context, options) => Audio(context, scope, () =>
            runtime.ApplySEffect(tracks, new AudioSEffectRequest(
                options.TargetId, options.InstanceId ?? "",
                options.Id ?? "", Arguments.Json<JsonElement>(options.Parameters), options.Scope))));
        registry.Register<AudioSEffectStopOptions>(BuiltinEntryIds.StopAudioSEffect, (context, options) => Audio(context, scope, () =>
            runtime.StopSEffect(tracks, options.TargetId, options.InstanceId ?? "", options.Scope, options.All)));
        registry.Register<AudioSEffectUpdateOptions>(BuiltinEntryIds.UpdateAudioSEffect, (context, options) => Audio(context, scope, () =>
            runtime.UpdateSEffect(tracks, options.TargetId, options.InstanceId ?? "", options.Parameters, options.Scope)));
        registry.Register<AudioLevelOptions>(BuiltinEntryIds.SetAudioLevel, (context, options) => Audio(context, scope, () =>
            runtime.SetLevel(tracks, new(options.TargetId, options.Scope, options.Gain, options.Muted))));
    }

    private static AudioPlayRequest PlayRequest(AudioPlayOptions options, AudioPlacement placement) => new(
        options.TrackId, options.Asset ?? "", placement, options.Gain, options.FadeIn, options.LoopCount, options.Completion, options.FadeOut);

    private static AudioPlaybackPrimitiveInstance Playback(PrimitiveCreateContext context, AudioRuntime runtime,
        AudioScope scope, AudioConfiguration tracks, AudioPlayOptions options, AudioPlacement placement) => new(() =>
        {
            scope.Bind(context.Runtime, context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            return runtime.Play(tracks, PlayRequest(options, placement));
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
            action(context, options.TrackId);
        });

    private static ImmediatePrimitiveInstance Audio(PrimitiveCreateContext context, AudioScope scope, Action action) =>
        new(() =>
        {
            scope.Bind(context.Runtime, context.ScopeCancellation);
            context.ScopeCancellation.ThrowIfCancellationRequested();
            action();
        }, batchId: context.BatchId);
}
