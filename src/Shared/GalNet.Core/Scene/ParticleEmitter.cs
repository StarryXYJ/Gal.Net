using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalNet.Core.Scene;

/// <summary>Platform-neutral authoring data for a scene particle emitter.</summary>
public sealed record ParticleEmitterDefinition(
    string ParticleTexture,
    float EmissionRate = 36,
    int MaxParticles = 160,
    int Seed = 20260911,
    ParticleInitialDefinition? Initial = null,
    ParticleMotionDefinition? Motion = null,
    ParticleLifetimeDefinition? Lifetime = null,
    ParticleShapeDefinition? Shape = null,
    ParticleFlipbookDefinition? Flipbook = null)
{
    [JsonIgnore] public ParticleInitialDefinition InitialModule => Initial ?? new ParticleInitialDefinition();
    [JsonIgnore] public ParticleMotionDefinition MotionModule => Motion ?? new ParticleMotionDefinition();
    [JsonIgnore] public ParticleLifetimeDefinition LifetimeModule => Lifetime ?? new ParticleLifetimeDefinition();

    public static ParticleEmitterDefinition FromJson(string parameters)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parameters) ? "{}" : parameters);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Particle emitter parameters must be a JSON object.");

            return new ParticleEmitterDefinition(
                Text(root, "particleTexture"),
                Math.Max(0, Number(root, "rate", 36)),
                Math.Max(1, Integer(root, "maxParticles", 160)),
                Integer(root, "seed", 20260911),
                InitialModuleFromJson(root),
                MotionModuleFromJson(root),
                LifetimeModuleFromJson(root),
                ShapeModule(root),
                FlipbookModule(root));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Particle emitter parameters must be a JSON object.", exception);
        }
    }

    private static ParticleInitialDefinition InitialModuleFromJson(JsonElement root)
    {
        if (!root.TryGetProperty("initial", out var value)) return new ParticleInitialDefinition();
        RequireObject(value, "Particle initial module");
        return new ParticleInitialDefinition
        {
            Lifetime = FloatRange(value, "lifetime", new ParticleFloatRange(3, 3), .01f),
            VelocityX = FloatRange(value, "velocityX", new ParticleFloatRange(-14, -14)),
            VelocityY = FloatRange(value, "velocityY", new ParticleFloatRange(96, 96)),
            Size = FloatRange(value, "size", new ParticleFloatRange(1, 1), .001f),
            RotationDegrees = FloatRange(value, "rotationDegrees", new ParticleFloatRange(0, 0)),
            AngularVelocityDegrees = FloatRange(value, "angularVelocityDegrees", new ParticleFloatRange(0, 0)),
            Color = ColorRange(value, "color", new ParticleColorRange())
        };
    }

    private static ParticleMotionDefinition MotionModuleFromJson(JsonElement root)
    {
        if (!root.TryGetProperty("motion", out var value)) return new ParticleMotionDefinition();
        RequireObject(value, "Particle motion module");
        ParticleAttractorDefinition? attractor = null;
        if (value.TryGetProperty("attractor", out var attractorValue))
        {
            RequireObject(attractorValue, "Particle attractor");
            attractor = new ParticleAttractorDefinition(
                Number(attractorValue, "x", 0),
                Number(attractorValue, "y", 0),
                Number(attractorValue, "strength", 0));
        }

        return new ParticleMotionDefinition
        {
            GravityX = Number(value, "gravityX", 0),
            GravityY = Number(value, "gravityY", 0),
            Noise = Math.Max(0, Number(value, "noise", 18)),
            Drag = Math.Max(0, Number(value, "drag", 0)),
            RadialVelocity = Number(value, "radialVelocity", 0),
            OrbitDegreesPerSecond = Number(value, "orbitDegreesPerSecond", 0),
            Attractor = attractor
        };
    }

    private static ParticleLifetimeDefinition LifetimeModuleFromJson(JsonElement root)
    {
        if (!root.TryGetProperty("lifetime", out var value)) return new ParticleLifetimeDefinition();
        RequireObject(value, "Particle lifetime module");
        return new ParticleLifetimeDefinition
        {
            Size = Curve(value, "size", static key => new ParticleCurveKey(Number(key, "time", 0), Number(key, "value", 1))),
            Opacity = Curve(value, "opacity", static key => new ParticleCurveKey(Number(key, "time", 0), Number(key, "value", 1))),
            Velocity = Curve(value, "velocity", static key => new ParticleCurveKey(Number(key, "time", 0), Number(key, "value", 1))),
            RotationDegrees = Curve(value, "rotationDegrees", static key => new ParticleCurveKey(Number(key, "time", 0), Number(key, "value", 0))),
            Color = Curve(value, "color", static key => new ParticleColorCurveKey(Number(key, "time", 0), Text(key, "color")))
        };
    }

    private static ParticleShapeDefinition? ShapeModule(JsonElement root)
    {
        if (!root.TryGetProperty("shape", out var value)) return null;
        RequireObject(value, "Particle shape");
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
        RequireObject(value, "Particle flipbook");

        var columns = Math.Max(1, Integer(value, "columns", 1));
        var rows = Math.Max(1, Integer(value, "rows", 1));
        var capacity = (long)columns * rows;
        if (capacity > int.MaxValue) throw new InvalidDataException("Particle flipbook grid is too large.");
        var frameCount = Math.Max(1, Integer(value, "frameCount", (int)capacity));
        if (frameCount > capacity) throw new InvalidDataException("Particle flipbook frameCount cannot exceed columns * rows.");

        var framesPerSecond = value.TryGetProperty("framesPerSecond", out _)
            ? FloatRange(value, "framesPerSecond", new ParticleFloatRange(0, 0), 0)
            : null;
        var cycles = value.TryGetProperty("cyclesOverLifetime", out _)
            ? FloatRange(value, "cyclesOverLifetime", new ParticleFloatRange(1, 1), 0)
            : new ParticleFloatRange(1, 1);
        var startFrame = IntRange(value, "startFrame", new ParticleIntRange(0, 0), 0);
        if (startFrame.Max >= frameCount)
            throw new InvalidDataException("Particle flipbook startFrame must be inside frameCount.");

        return new ParticleFlipbookDefinition(columns, rows, frameCount, framesPerSecond, cycles, Boolean(value, "loop", false), startFrame);
    }

    private static ParticleFloatRange FloatRange(JsonElement root, string name, ParticleFloatRange fallback, float? minimum = null)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        RequireObject(value, $"Particle {name} range");
        var min = 0f;
        var max = 0f;
        var hasMin = value.TryGetProperty("min", out var minValue) && minValue.TryGetSingle(out min);
        var hasMax = value.TryGetProperty("max", out var maxValue) && maxValue.TryGetSingle(out max);
        if (!hasMin && !hasMax) throw new InvalidDataException($"Particle {name} range requires min or max.");
        if (!hasMin) min = max;
        if (!hasMax) max = min;
        if (!float.IsFinite(min) || !float.IsFinite(max) || max < min || minimum is not null && min < minimum)
            throw new InvalidDataException($"Particle {name} range is invalid.");
        return new ParticleFloatRange(min, max);
    }

    private static ParticleIntRange IntRange(JsonElement root, string name, ParticleIntRange fallback, int? minimum = null)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        RequireObject(value, $"Particle {name} range");
        var min = 0;
        var max = 0;
        var hasMin = value.TryGetProperty("min", out var minValue) && minValue.TryGetInt32(out min);
        var hasMax = value.TryGetProperty("max", out var maxValue) && maxValue.TryGetInt32(out max);
        if (!hasMin && !hasMax) throw new InvalidDataException($"Particle {name} range requires min or max.");
        if (!hasMin) min = max;
        if (!hasMax) max = min;
        if (max < min || minimum is not null && min < minimum)
            throw new InvalidDataException($"Particle {name} range is invalid.");
        return new ParticleIntRange(min, max);
    }

    private static ParticleColorRange ColorRange(JsonElement root, string name, ParticleColorRange fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        RequireObject(value, $"Particle {name} range");
        var minimum = Text(value, "min");
        var maximum = Text(value, "max");
        if (string.IsNullOrWhiteSpace(minimum)) minimum = maximum;
        if (string.IsNullOrWhiteSpace(maximum)) maximum = minimum;
        if (string.IsNullOrWhiteSpace(minimum)) throw new InvalidDataException($"Particle {name} range requires min or max.");
        return new ParticleColorRange(minimum, maximum);
    }

    private static T[]? Curve<T>(JsonElement root, string name, Func<JsonElement, T> read)
    {
        if (!root.TryGetProperty(name, out var values)) return null;
        if (values.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"Particle {name} curve must be an array.");
        return values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).Select(read).ToArray();
    }

    private static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{name} must be a JSON object.");
    }

    private static float Number(JsonElement root, string name, float fallback) => root.TryGetProperty(name, out var value) && value.TryGetSingle(out var result) ? result : fallback;
    private static int Integer(JsonElement root, string name, int fallback) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    private static bool Boolean(JsonElement root, string name, bool fallback) => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
}

public sealed record ParticleFloatRange(float Min = 0, float Max = 0)
{
    [JsonIgnore] public float Center => (Min + Max) * .5f;
    [JsonIgnore] public float HalfWidth => (Max - Min) * .5f;
    public ParticleFloatRange WithCenter(float center) => new(center - HalfWidth, center + HalfWidth);
}

public sealed record ParticleIntRange(int Min = 0, int Max = 0);
public sealed record ParticleColorRange(string Min = "#FFFFFFFF", string Max = "#FFFFFFFF");

public sealed record ParticleInitialDefinition
{
    public ParticleFloatRange Lifetime { get; init; } = new(3, 3);
    public ParticleFloatRange VelocityX { get; init; } = new(-14, -14);
    public ParticleFloatRange VelocityY { get; init; } = new(96, 96);
    public ParticleFloatRange Size { get; init; } = new(1, 1);
    public ParticleFloatRange RotationDegrees { get; init; } = new(0, 0);
    public ParticleFloatRange AngularVelocityDegrees { get; init; } = new(0, 0);
    public ParticleColorRange Color { get; init; } = new();
}

public sealed record ParticleMotionDefinition
{
    public float GravityX { get; init; }
    public float GravityY { get; init; }
    public float Noise { get; init; } = 18;
    public float Drag { get; init; }
    public float RadialVelocity { get; init; }
    public float OrbitDegreesPerSecond { get; init; }
    public ParticleAttractorDefinition? Attractor { get; init; }
}

public sealed record ParticleAttractorDefinition(float X = 0, float Y = 0, float Strength = 0);

public sealed record ParticleLifetimeDefinition
{
    public IReadOnlyList<ParticleCurveKey>? Size { get; init; }
    public IReadOnlyList<ParticleCurveKey>? Opacity { get; init; }
    public IReadOnlyList<ParticleCurveKey>? Velocity { get; init; }
    public IReadOnlyList<ParticleCurveKey>? RotationDegrees { get; init; }
    public IReadOnlyList<ParticleColorCurveKey>? Color { get; init; }
}

public sealed record ParticleCurveKey(float Time, float Value);
public sealed record ParticleColorCurveKey(float Time, string Color);

/// <summary>Per-particle row-major sprite-sheet playback driven by particle age.</summary>
public sealed record ParticleFlipbookDefinition(
    int Columns = 1,
    int Rows = 1,
    int FrameCount = 1,
    ParticleFloatRange? FramesPerSecond = null,
    ParticleFloatRange? CyclesOverLifetime = null,
    bool Loop = false,
    ParticleIntRange? StartFrame = null)
{
    public long Capacity => (long)Columns * Rows;
    public bool IsValid => Columns > 0 && Rows > 0 && FrameCount is > 0 && FrameCount <= Capacity;
    [JsonIgnore] public bool UsesFramesPerSecond => FramesPerSecond is { Max: > 0 };
    [JsonIgnore] public ParticleFloatRange PlaybackRate => UsesFramesPerSecond ? FramesPerSecond! : CyclesOverLifetime ?? new ParticleFloatRange(1, 1);
    [JsonIgnore] public ParticleIntRange StartFrameRange => StartFrame ?? new ParticleIntRange(0, 0);

    public int GetFrameIndex(float age, float lifetime, float playbackRate, int startFrame = 0)
    {
        if (!IsValid) return 0;
        var elapsedFrames = UsesFramesPerSecond
            ? Math.Max(0, age) * Math.Max(0, playbackRate)
            : Math.Clamp(age / Math.Max(.0001f, lifetime), 0, 1) * Math.Max(0, playbackRate) * FrameCount;
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
        ["emissionRate"] = definition.EmissionRate,
        ["initialVelocityX"] = definition.InitialModule.VelocityX.Center,
        ["initialVelocityY"] = definition.InitialModule.VelocityY.Center,
        ["noise"] = definition.MotionModule.Noise,
        ["particleScale"] = definition.InitialModule.Size.Center,
        ["particleLifetime"] = definition.InitialModule.Lifetime.Center
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
