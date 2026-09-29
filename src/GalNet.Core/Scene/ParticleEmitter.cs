using System.Text.Json;

namespace GalNet.Core.Scene;

/// <summary>Platform-neutral authoring data for a scene particle emitter.</summary>
public sealed record ParticleEmitterDefinition(
    string ParticleTexture,
    float EmissionRate = 36,
    int MaxParticles = 160,
    float InitialVelocityX = -14,
    float InitialVelocityY = 96,
    float Noise = 18,
    float ParticleScale = 1,
    float ParticleLifetime = 3,
    int Seed = 20260911,
    float GravityX = 0,
    float GravityY = 0,
    IReadOnlyList<ParticleCurveKey>? SizeCurve = null,
    IReadOnlyList<ParticleColorCurveKey>? ColorCurve = null)
{
    public static ParticleEmitterDefinition FromJson(string parameters)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parameters) ? "{}" : parameters);
            var root = document.RootElement;
            static float Number(JsonElement root, string name, float fallback) => root.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;
            static int Integer(JsonElement root, string name, int fallback) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
            static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            return new ParticleEmitterDefinition(
                Text(root, "particleTexture"),
                Math.Max(0, Number(root, "emissionRate", 36)),
                Math.Max(1, Integer(root, "maxParticles", 160)),
                Number(root, "initialVelocityX", -14), Number(root, "initialVelocityY", 96),
                Math.Max(0, Number(root, "noise", 18)), Math.Max(.001f, Number(root, "particleScale", 1)),
                Math.Max(.01f, Number(root, "particleLifetime", 3)), Integer(root, "seed", 20260911),
                Number(root, "gravityX", 0), Number(root, "gravityY", 0),
                Curve(root, "sizeCurve", static value => new ParticleCurveKey(Number(value, "time", 0), Number(value, "value", 1))),
                Curve(root, "colorCurve", static value => new ParticleColorCurveKey(Number(value, "time", 0), Text(value, "color"))));
        }
        catch (JsonException exception) { throw new InvalidDataException("Particle emitter parameters must be a JSON object.", exception); }
    }

    private static IReadOnlyList<T>? Curve<T>(JsonElement root, string name, Func<JsonElement, T> read)
    {
        if (!root.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array) return null;
        return values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).Select(read).ToArray();
    }
}

public sealed record ParticleCurveKey(float Time, float Value);
public sealed record ParticleColorCurveKey(float Time, string Color);

/// <summary>Runtime-owned, animatable emitter handle. Live particles remain renderer-owned.</summary>
public sealed class ParticleEmitterInstance : AnimatableSceneInstance
{
    private readonly Dictionary<string, float> _values;
    public static IReadOnlyList<AnimatableProperty> AnimationProperties { get; } =
    [
        new("emissionRate", AnimationValueKind.Float, 0), new("initialVelocityX", AnimationValueKind.Float),
        new("initialVelocityY", AnimationValueKind.Float), new("noise", AnimationValueKind.Float, 0),
        new("particleScale", AnimationValueKind.Float, .001f), new("particleLifetime", AnimationValueKind.Float, .01f)
    ];

    public ParticleEmitterInstance()
    {
        Definition = new ParticleEmitterDefinition("");
        _values = CreateValues(Definition);
    }

    public ParticleEmitterInstance(string id, ParticleEmitterDefinition definition, float z)
    {
        Id = id; Definition = definition; Z = z; _values = CreateValues(definition);
    }

    public override string Id { get; init; } = "";
    public ParticleEmitterDefinition Definition { get; init; }
    public float Z { get; init; }
    public bool IsEmitting { get; set; } = true;
    public override IReadOnlyList<AnimatableProperty> AnimatableProperties => AnimationProperties;
    public IReadOnlyDictionary<string, float> AnimationValues => _values;
    public override bool TryGetAnimationValue(string propertyName, out float value) => _values.TryGetValue(propertyName, out value);
    public override bool TrySetAnimationValue(string propertyName, float value, out string? error)
    {
        var property = AnimationProperties.FirstOrDefault(candidate => candidate.Name == propertyName);
        if (property is null || !property.Accepts(value)) { error = $"Invalid particle emitter animation property '{propertyName}'."; return false; }
        _values[propertyName] = value; error = null; return true;
    }
    public void RestoreAnimationValues(IEnumerable<KeyValuePair<string, float>> values)
    {
        foreach (var (name, value) in values)
            if (!TrySetAnimationValue(name, value, out var error)) throw new InvalidDataException(error);
    }

    private static Dictionary<string, float> CreateValues(ParticleEmitterDefinition definition) => new(StringComparer.Ordinal)
    {
        ["emissionRate"] = definition.EmissionRate, ["initialVelocityX"] = definition.InitialVelocityX,
        ["initialVelocityY"] = definition.InitialVelocityY, ["noise"] = definition.Noise,
        ["particleScale"] = definition.ParticleScale, ["particleLifetime"] = definition.ParticleLifetime
    };
}

/// <summary>Saved state for an emitter that was still producing particles at save time.</summary>
public sealed class ActiveParticleEmitterState
{
    public string InstanceId { get; init; } = "";
    public ParticleEmitterDefinition Definition { get; init; } = new("");
    public float Z { get; init; }
    public Dictionary<string, float> AnimationValues { get; init; } = new(StringComparer.Ordinal);
}
