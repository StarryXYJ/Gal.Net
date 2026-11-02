using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;
using GalNet.Core.Runtime;

namespace GalNet.Primitives.Builtins;

public sealed partial class BuiltinAudioModule
{
    private readonly Dictionary<string, string> _automationOwners = new(StringComparer.Ordinal);

    internal void BindTimeline(IGameContext game, TimelineDefinition timeline, CancellationToken token)
    {
        _scope.Bind(game, token);
        lock (_automationOwners)
            foreach (var track in timeline.AudioTracks) _automationOwners[track.Identity] = timeline.PlaybackHandleId;
    }

    internal void SampleTimeline(IGameContext game, TimelineDefinition timeline, double frame)
    {
        foreach (var track in timeline.AudioTracks)
        {
            lock (_automationOwners)
                if (_automationOwners.GetValueOrDefault(track.Identity) != timeline.PlaybackHandleId) continue;
            var value = TimelineSampler.Evaluate(new() { Keys = track.Keys }, frame);
            if (string.IsNullOrEmpty(track.InstanceId))
            {
                var key = new AudioChannelState(track.TargetId, track.Scope).Key();
                var muted = game.State.Table<AudioChannelState>().TryGet(key, out var level) && level.Muted;
                _runtime.SetLevel(_configuration, new(track.TargetId, track.Scope, value, muted));
            }
            else _runtime.UpdateSEffect(_configuration, track.TargetId, track.InstanceId,
                GalNet.Core.Serialization.JsonCodec.SerializeToElement(new Dictionary<string, float> { [track.Parameter] = value }), track.Scope);
        }
    }

    internal void CompleteTimeline(IGameContext game, TimelineDefinition timeline, bool catchUp, bool initialLoop = false)
    {
        // Persistent control events reach their terminal state; transient sounds never catch up.
        if (catchUp)
            foreach (var item in timeline.Events.OrderBy(item => item.Frame))
                if (item.Type != BuiltinEntryIds.PlayAudio && (!initialLoop || item.Frame <= 0 && item.Execution != TimelineEventExecution.OnCompletion)) ExecuteTimelineEvent(item);
        SampleTimeline(game, timeline, initialLoop ? 0 : timeline.DurationFrames);
        ReleaseTimeline(timeline);
    }

    internal void ReleaseTimeline(TimelineDefinition timeline)
    {
        lock (_automationOwners)
            foreach (var track in timeline.AudioTracks)
                if (_automationOwners.GetValueOrDefault(track.Identity) == timeline.PlaybackHandleId) _automationOwners.Remove(track.Identity);
    }

    internal void ExecuteTimelineEvent(TimelineEventDefinition item)
    {
        var json = GalNet.Core.Serialization.JsonCodec.SerializeToElement(item.Parameters);
        switch (item.Type)
        {
            case BuiltinEntryIds.PlayAudio:
                var play = ValueCodec.DeserializeFields<AudioPlayOptions>(AudioPlayOptions.Schema, json);
                _runtime.Play(_configuration, PlayRequest(play, play.Placement ?? AudioPlacement.Replace)); break;
            case BuiltinEntryIds.StopAudio:
                var stop = ValueCodec.DeserializeFields<AudioStopOptions>(AudioStopOptions.Schema, json);
                _runtime.Stop(_configuration, new(stop.TrackId, stop.ClearQueue ?? true, stop.FadeOut)); break;
            case BuiltinEntryIds.ApplyAudioSEffect:
                var apply = ValueCodec.DeserializeFields<AudioSEffectOptions>(AudioSEffectOptions.Schema, json);
                _runtime.ApplySEffect(_configuration, new(apply.TargetId, apply.InstanceId ?? "", apply.Id ?? "", apply.Parameters, apply.Scope)); break;
            case BuiltinEntryIds.UpdateAudioSEffect:
                var update = ValueCodec.DeserializeFields<AudioSEffectUpdateOptions>(AudioSEffectUpdateOptions.Schema, json);
                _runtime.UpdateSEffect(_configuration, update.TargetId, update.InstanceId ?? "", update.Parameters, update.Scope); break;
            case BuiltinEntryIds.StopAudioSEffect:
                var remove = ValueCodec.DeserializeFields<AudioSEffectStopOptions>(AudioSEffectStopOptions.Schema, json);
                _runtime.StopSEffect(_configuration, remove.TargetId, remove.InstanceId ?? "", remove.Scope, remove.All); break;
            case BuiltinEntryIds.SetAudioLevel:
                var mix = ValueCodec.DeserializeFields<AudioLevelOptions>(AudioLevelOptions.Schema, json);
                _runtime.SetLevel(_configuration, new(mix.TargetId, mix.Scope, mix.Gain, mix.Muted)); break;
        }
    }
}
