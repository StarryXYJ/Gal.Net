using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class SceneTargetAnalysis
{
    private static void AddPlayback(State state, string group, EditorEntryData owner, string handle, string slot, TargetPresence presence)
    {
        // A missing optional identity is unaddressable; querying must never manufacture one.
        if (string.IsNullOrWhiteSpace(handle)) return;
        var locator = new SceneLocator(group, owner.StableId, slot);
        var target = new SceneTarget(locator, handle, SceneTargetKind.Playback, owner.Type, presence, [], EmptyValues);
        if (state.Targets.ContainsKey(handle) || state.Playbacks.TryGetValue(handle, out var previous) && (previous.Locator != locator || previous.Kind == SceneTargetKind.Unresolved))
        {
            target = target with { Kind = SceneTargetKind.Unresolved, Presence = TargetPresence.Possible, Resource = "" };
            state.Complete = false;
        }
        state.Playbacks[handle] = target;
    }
    private static void TransferAnimationPlayback(State state, string group, EditorEntryData entry)
    {
        var options = JsonSerializer.SerializeToElement(entry.Parameters).Deserialize<AnimationOptions>(TimelineJson)!;
        if (!float.IsFinite(options.Duration) || options.Duration < 0 || !float.IsFinite(options.From) || !float.IsFinite(options.To)
            || !Enum.IsDefined(options.LoopMode ?? AnimationLoopMode.Once) || !Enum.IsDefined(options.BlendMode ?? AnimationBlendMode.Replace)
            || !Enum.IsDefined(options.Curve ?? BuiltinAnimationCurve.Linear)) throw new InvalidDataException("Invalid animation playback.");
        if ((options.LoopMode ?? AnimationLoopMode.Once) == AnimationLoopMode.Once && (options.Blocking == true || options.Duration == 0)) return;
        AddPlayback(state, group, entry, options.PlaybackHandleId ?? "", "playbackHandleId", TargetPresence.Possible);
    }
    private static void StopPlayback(State state, EditorEntryData entry)
    {
        var options = JsonSerializer.SerializeToElement(entry.Parameters).Deserialize<AnimationStopOptions>(TimelineJson)!;
        var mode = options.Mode ?? AnimationStopMode.AfterIteration;
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Invalid stop mode.");
        if (mode == AnimationStopMode.CompleteImmediately) state.Playbacks.Remove(options.PlaybackHandleId ?? "");
        else if (state.Playbacks.TryGetValue(options.PlaybackHandleId ?? "", out var target))
            state.Playbacks[target.Handle] = target with { Presence = TargetPresence.Possible };
    }
}
