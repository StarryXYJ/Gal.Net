using GalNet.Core.Scene;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Runtime;

/// <summary>Copies persistent state without sharing mutable objects or playback identities.</summary>
public static class SnapshotCopy
{
    public static GameSnapshot Clone(GameSnapshot snapshot) => new()
    {
        Version = snapshot.Version,
        NodeId = snapshot.NodeId,
        EntryIndex = snapshot.EntryIndex,
        Variables = snapshot.Variables.ToDictionary(pair => pair.Key, pair => CloneVariable(pair.Value), StringComparer.Ordinal),
        SceneState = CloneSceneState(snapshot.SceneState)
    };

    public static GalVariable CloneVariable(GalVariable variable)
    {
        var copy = new GalVariable { Uid = variable.Uid, Name = variable.Name };
        switch (variable.Type)
        {
            case VariableType.Bool: copy.SetValue(variable.AsBool()); break;
            case VariableType.Int: copy.SetValue(variable.AsInt()); break;
            case VariableType.Float: copy.SetValue(variable.AsFloat()); break;
            default: copy.SetValue(variable.AsString()); break;
        }
        return copy;
    }

    public static SceneState CloneSceneState(SceneState source) => new()
    {
        Layers = source.Layers.Select(layer => new Layer
        {
            Id = layer.Id,
            AssetId = layer.AssetId,
            IsVideo = layer.IsVideo,
            Flipbook = layer.Flipbook?.Clone(),
            Color = layer.Color,
            Transform = layer.Transform.Clone(),
            Z = layer.Z,
            DisplayMode = layer.DisplayMode,
            Visible = layer.Visible,
            Opacity = layer.Opacity
        }).ToList(),
        ActiveEffects = source.ActiveEffects.Select(effect => new ActiveEffectState
        {
            Id = effect.Id,
            ProgramResource = effect.ProgramResource,
            InstanceId = effect.InstanceId,
            TargetHandleId = effect.TargetHandleId,
            Order = effect.Order,
            Parameters = effect.Parameters,
            AnimationValues = effect.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }).ToList(),
        ActiveParticleEmitters = source.ActiveParticleEmitters.Select(emitter => new ActiveParticleEmitterState
        {
            InstanceId = emitter.InstanceId,
            Definition = CloneParticleDefinition(emitter.Definition),
            Z = emitter.Z,
            AnimationValues = emitter.AnimationValues.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }).ToList(),
        ActiveAnimations = source.ActiveAnimations.Select(animation => new ActiveAnimationState
        {
            EntryType = animation.EntryType,
            PlaybackHandleId = animation.PlaybackHandleId,
            LoopMode = animation.LoopMode,
            Parameters = animation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        }).ToList()
    };

    private static ParticleEmitterDefinition CloneParticleDefinition(ParticleEmitterDefinition definition) => definition with
    {
        // All other modules and curve keys are immutable records. Lifetime's
        // collection interfaces can wrap mutable author-owned arrays or lists.
        Lifetime = definition.Lifetime is { } lifetime ? lifetime with
        {
            Size = lifetime.Size?.ToArray(),
            Opacity = lifetime.Opacity?.ToArray(),
            Velocity = lifetime.Velocity?.ToArray(),
            RotationDegrees = lifetime.RotationDegrees?.ToArray(),
            Color = lifetime.Color?.ToArray()
        } : null
    };

}
