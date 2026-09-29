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
    IReadOnlyList<ParticleColorCurveKey>? ColorCurve = null,
    ParticleShapeDefinition? Shape = null,
    ParticleFlipbookDefinition? Flipbook = null)
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
            var shape = ShapeModule(root);
            var flipbook = FlipbookModule(root);
            return new ParticleEmitterDefinition(
                Text(root, "particleTexture"),
                Math.Max(0, Number(root, "rate", 36)),
                Math.Max(1, Integer(root, "maxParticles", 160)),
                Number(root, "initialVelocityX", -14), Number(root, "initialVelocityY", 96),
                Math.Max(0, Number(root, "noise", 18)), Math.Max(.001f, Number(root, "particleScale", 1)),
                Math.Max(.01f, Number(root, "particleLifetime", 3)), Integer(root, "seed", 20260911),
                Number(root, "gravityX", 0), Number(root, "gravityY", 0),
                Curve(root, "sizeCurve", static value => new ParticleCurveKey(Number(value, "time", 0), Number(value, "value", 1))),
                Curve(root, "colorCurve", static value => new ParticleColorCurveKey(Number(value, "time", 0), Text(value, "color"))),
                shape,
                flipbook);
        }
        catch (JsonException exception) { throw new InvalidDataException("Particle emitter parameters must be a JSON object.", exception); }
    }

    private static ParticleShapeDefinition? ShapeModule(JsonElement root)
    {
        if (!root.TryGetProperty("shape", out var value) || value.ValueKind != JsonValueKind.Object) return null;
        var type = Text(value, "type").ToLowerInvariant() switch
        {
            "point" => ParticleShapeKind.Point,
            "box" => ParticleShapeKind.Box,
            "circle" => ParticleShapeKind.Circle,
            "line" => ParticleShapeKind.Line,
            var invalid => throw new InvalidDataException($"Unsupported particle shape type '{invalid}'.")
        };
        return new ParticleShapeDefinition(
            type,
            Number(value, "x", 0),
            Number(value, "y", 0),
            Math.Max(0, Number(value, "width", 0)),
            Math.Max(0, Number(value, "height", 0)),
            Math.Max(0, Number(value, "radius", 0)),
            Number(value, "endX", 0),
            Number(value, "endY", 0));
    }

    private static ParticleFlipbookDefinition? FlipbookModule(JsonElement root)
    {
        if (!root.TryGetProperty("flipbook", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Particle flipbook must be a JSON object.");

        var columns = Math.Max(1, Integer(value, "columns", 1));
        var rows = Math.Max(1, Integer(value, "rows", 1));
        var capacity = (long)columns * rows;
        if (capacity > int.MaxValue)
            throw new InvalidDataException("Particle flipbook grid is too large.");
        var frameCount = Math.Max(1, Integer(value, "frameCount", (int)capacity));
        if (frameCount > capacity)
            throw new InvalidDataException("Particle flipbook frameCount cannot exceed columns * rows.");

        return new ParticleFlipbookDefinition(
            columns,
            rows,
            frameCount,
            Math.Max(0, Number(value, "framesPerSecond", 0)),
            Math.Max(0, Number(value, "cyclesOverLifetime", 1)),
            Boolean(value, "loop", false),
            Boolean(value, "randomStartFrame", false));
    }

    private static IReadOnlyList<T>? Curve<T>(JsonElement root, string name, Func<JsonElement, T> read)
    {
        if (!root.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array) return null;
        return values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).Select(read).ToArray();
    }

    private static float Number(JsonElement root, string name, float fallback) => root.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;
    private static int Integer(JsonElement root, string name, int fallback) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    private static bool Boolean(JsonElement root, string name, bool fallback) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
}

public sealed record ParticleCurveKey(float Time, float Value);
public sealed record ParticleColorCurveKey(float Time, string Color);

/// <summary>Per-particle row-major sprite-sheet playback driven by particle age.</summary>
public sealed record ParticleFlipbookDefinition(
    int Columns = 1,
    int Rows = 1,
    int FrameCount = 1,
    float FramesPerSecond = 0,
    float CyclesOverLifetime = 1,
    bool Loop = false,
    bool RandomStartFrame = false)
{
    public long Capacity => (long)Columns * Rows;
    public bool IsValid => Columns > 0 && Rows > 0 && FrameCount is > 0 && FrameCount <= Capacity;

    public int GetFrameIndex(float age, float lifetime, int startFrame = 0)
    {
        if (!IsValid) return 0;
        var elapsedFrames = FramesPerSecond > 0
            ? Math.Max(0, age) * FramesPerSecond
            : Math.Clamp(age / Math.Max(.0001f, lifetime), 0, 1) * Math.Max(0, CyclesOverLifetime) * FrameCount;
        var frame = Math.Max(0, startFrame) + (int)MathF.Floor(elapsedFrames);
        return Loop ? frame % FrameCount : Math.Clamp(frame, 0, FrameCount - 1);
    }
}

public enum ParticleShapeKind { Point, Box, Circle, Line }

/// <summary>Pixel-space spawn shape. X/Y is the center except for Line, where it is the start point.</summary>
public sealed record ParticleShapeDefinition(
    ParticleShapeKind Type = ParticleShapeKind.Point,
    float X = 0,
    float Y = 0,
    float Width = 0,
    float Height = 0,
    float Radius = 0,
    float EndX = 0,
    float EndY = 0);

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
