using System.Text.Json;
using GalNet.Core.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Primitives.Builtins;

internal static partial class BuiltinRuntimeActions
{
    private static string Get(Dictionary<string, string> parameters, string name, string fallback = "") =>
        parameters.TryGetValue(name, out var value) ? value : fallback;

    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static float ParseFloat(string value, float fallback = 0) =>
        TryParseFloat(value, out var result) ? result : fallback;

    private static double ParseDouble(string value, double fallback = 0) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;

    private static bool ParseBool(string value, bool fallback = false) =>
        bool.TryParse(value, out var result) ? result : fallback;

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(value, true, out var result) ? result : fallback;

    private static string Invariant(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string Invariant(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static LayerRenderRequest ToLayerRenderRequest(
        IReadOnlyDictionary<string, JsonElement> parameters,
        string? color) => new(
            Get(parameters, "handleId"),
            Get(parameters, "assetId"),
            Json<LayerTransform>(parameters, "transform") ?? new LayerTransform(),
            Float(parameters, "z"),
            Enum(parameters, "displayMode", LayerDisplayMode.Native),
            Float(parameters, "opacity", 1),
            color,
            Json<FlipbookDefinition>(parameters, "flipbook")?.Clone());

    private static EffectRequest ToEffectRequest(IReadOnlyDictionary<string, JsonElement> parameters) => new(
        Get(parameters, "id"),
        Get(parameters, "instanceId"),
        Get(parameters, "targetHandleId"),
        Int(parameters, "order"),
        RawJson(parameters, "parameters", "{}"),
        Get(parameters, "program"));

    private static ParticleBurstRequest ToParticleBurstRequest(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var count = Int(parameters, "count", 1);
        if (count <= 0) throw new InvalidDataException("particle.burst requires a positive 'count'.");
        var definition = ReadParticleDefinition(RawJson(parameters, "parameters", "{}"), BurstParticlesEntry.TypeId) with { EmissionRate = 0 };
        return new ParticleBurstRequest(definition, count, Float(parameters, "z", 100));
    }

    private static ParticleEmitterRequest ToParticleEmitterRequest(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var instanceId = Get(parameters, "instanceId");
        if (string.IsNullOrWhiteSpace(instanceId)) throw new InvalidDataException("particle.play requires 'instanceId'.");
        var definition = ReadParticleDefinition(RawJson(parameters, "parameters", "{}"), PlayParticleEmitterEntry.TypeId);
        return new ParticleEmitterRequest(instanceId, definition, Float(parameters, "z", 100));
    }

    private static ParticleEmitterDefinition ReadParticleDefinition(string json, string entryType)
    {
        var definition = ParticleEmitterDefinition.FromJson(json);
        if (string.IsNullOrWhiteSpace(definition.ParticleTexture))
            throw new InvalidDataException($"{entryType} requires 'parameters.particleTexture'.");
        return definition;
    }

    private static string Get(IReadOnlyDictionary<string, JsonElement> parameters, string name, string fallback = "") =>
        parameters.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static float Float(IReadOnlyDictionary<string, JsonElement> parameters, string name, float fallback = 0) =>
        parameters.TryGetValue(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;

    private static int Int(IReadOnlyDictionary<string, JsonElement> parameters, string name, int fallback = 0) =>
        parameters.TryGetValue(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static TEnum Enum<TEnum>(IReadOnlyDictionary<string, JsonElement> parameters, string name, TEnum fallback)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(Get(parameters, name), true, out var result) ? result : fallback;

    private static string RawJson(IReadOnlyDictionary<string, JsonElement> parameters, string name, string fallback = "{}") =>
        parameters.TryGetValue(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText()
            : fallback;

    private static T? Json<T>(IReadOnlyDictionary<string, JsonElement> parameters, string name) =>
        parameters.TryGetValue(name, out var value)
            ? value.Deserialize<T>(Arguments.JsonOptions)
            : default;
}
