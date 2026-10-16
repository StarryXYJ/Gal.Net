using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Scene;

namespace GalNet.Contracts.Builtins;

/// <summary>Portable semantic checks shared by compilation and every implementation of built-in contracts.</summary>
internal static class BuiltinArgumentValidation
{
    private static readonly Lazy<IEntryCatalog> EventCatalog = new(() => new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
    private static readonly HashSet<string> PlanEventTypes = new(StringComparer.Ordinal)
    {
        BuiltinEntryIds.ShowLayer, BuiltinEntryIds.ShowColorLayer, BuiltinEntryIds.HideLayer,
        BuiltinEntryIds.MoveLayer, BuiltinEntryIds.ReplaceLayer, BuiltinEntryIds.ApplyEffect,
        BuiltinEntryIds.StopEffect, BuiltinEntryIds.PlayParticleEmitter,
        BuiltinEntryIds.StopParticleEmitter, BuiltinEntryIds.BurstParticles
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Validate(string type, JsonElement arguments)
    {
        switch (type)
        {
            case BuiltinEntryIds.PlayVideo:
                RequireText(type, arguments, "asset", "handleId");
                var points = arguments.GetProperty("skipPoints").Deserialize<double[]>(JsonOptions)
                    ?? throw new InvalidDataException("video.play skipPoints must be an array.");
                if (points.Any(point => !double.IsFinite(point) || point < 0) ||
                    points.Zip(points.Skip(1), (left, right) => right > left).Any(valid => !valid))
                    throw new InvalidDataException("video.play skipPoints must be finite, non-negative and strictly increasing seconds.");
                break;
            case BuiltinEntryIds.PlayAnimationPlan:
                if (!arguments.TryGetProperty("plan", out var value) || value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("animation.play requires a plan object.");
                ValidatePlan(value.Deserialize<AnimationPlanDefinition>(JsonOptions)!);
                break;
            case BuiltinEntryIds.PlayParticleEmitter:
            case BuiltinEntryIds.BurstParticles:
                if (type == BuiltinEntryIds.PlayParticleEmitter) RequireText(type, arguments, "instanceId");
                var definition = ParticleEmitterDefinition.FromJson(arguments.GetProperty("parameters").GetRawText());
                if (string.IsNullOrWhiteSpace(definition.ParticleTexture))
                    throw new InvalidDataException($"{type} requires 'parameters.particleTexture'.");
                break;
        }
    }

    private static void RequireText(string type, JsonElement arguments, params string[] names)
    {
        foreach (var name in names)
            if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException($"{type} requires '{name}'.");
    }

    private static void ValidatePlan(AnimationPlanDefinition plan)
    {
        if (plan.FrameRate is < 1 or > 240 || plan.DurationFrames < 0)
            throw new InvalidDataException("Animation plan frameRate must be 1..240 and durationFrames must be non-negative.");
        if (plan.Tracks is null || plan.Events is null || !Enum.IsDefined(plan.LoopMode))
            throw new InvalidDataException("Animation plan requires track/event arrays and a valid loop mode.");
        var targets = new HashSet<(string HandleId, string Property)>();
        foreach (var track in plan.Tracks)
        {
            if (track is null || track.Keys is null || !Enum.IsDefined(track.BlendMode) ||
                string.IsNullOrWhiteSpace(track.HandleId) || string.IsNullOrWhiteSpace(track.Property) ||
                !targets.Add((track.HandleId, track.Property)) || track.Keys.Count == 0 || track.Keys[0] is null || track.Keys[0].Frame != 0)
                throw new InvalidDataException("Animation tracks require unique targets and a key at frame zero.");
            var previous = -1;
            foreach (var key in track.Keys)
            {
                if (key is null || !Enum.IsDefined(key.InterpolationToNext) || key.Frame <= previous || key.Frame > plan.DurationFrames ||
                    !float.IsFinite(key.Value) || !float.IsFinite(key.InTangent) || !float.IsFinite(key.OutTangent))
                    throw new InvalidDataException("Animation keys must be finite, strictly ordered and inside the plan duration.");
                previous = key.Frame;
            }
        }
        foreach (var item in plan.Events)
        {
            if (item is null || item.Parameters is null || item.Frame < 0 || item.Frame > plan.DurationFrames)
                throw new InvalidDataException("Animation events must be inside the plan duration.");
            if (!PlanEventTypes.Contains(item.Type))
                throw new InvalidDataException($"Unsupported animation plan event '{item.Type}'.");
            PrimitiveArgumentHelper.Normalize(EventCatalog.Value.Get(item.Type).DynamicParameters!,
                JsonSerializer.SerializeToElement(item.Parameters));
        }
    }
}
