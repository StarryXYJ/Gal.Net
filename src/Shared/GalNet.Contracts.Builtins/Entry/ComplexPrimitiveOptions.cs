using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Parameters;
using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class LayerShowOptions
{
    [Common, Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? HandleId { get; set; }
    [Common, Field(1, AllowNull = true, EditorId = "entry.imageasset")] public string? AssetId { get; set; }
    [Field(2, EditorId = ValueEditorIds.Json)] public JsonElement Flipbook { get; set; }
    [Field(3, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(4, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 0;
    [Field(5, EditorId = ValueEditorIds.Numeric)] public float Opacity { get; set; } = 1;
    [Field(6, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? DisplayMode { get; set; } = LayerDisplayMode.Native;
}

[GenerateFieldSchema]
public partial class LayerColorOptions
{
    [Common, Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? HandleId { get; set; }
    [Common, Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? Color { get; set; }
    [Field(2, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(3, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 1000;
    [Field(4, EditorId = ValueEditorIds.Numeric)] public float Opacity { get; set; } = 1;
}

[GenerateFieldSchema]
public partial class LayerMoveOptions
{
    [Common, Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? HandleId { get; set; }
    [Field(1, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(2, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 0;
    [Common, Field(3, Minimum = 0, EditorId = ValueEditorIds.Numeric)] public float Duration { get; set; } = 0.5f;
}

[GenerateFieldSchema]
public partial class AnimationOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? PlaybackHandleId { get; set; }
    [Common, Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? HandleId { get; set; }
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(AnimationPropertyValueConverter))]
    [Common]
    public string? Property { get; set; }
    [Common, Field(3, EditorId = ValueEditorIds.Numeric)] public float From { get; set; }
    [Common, Field(4, EditorId = ValueEditorIds.Numeric)] public float To { get; set; }
    [Common, Field(5, Minimum = 0, EditorId = ValueEditorIds.Numeric)] public float Duration { get; set; } = 0.25f;
    [Field(6, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<BuiltinAnimationCurve>))]
    public BuiltinAnimationCurve? Curve { get; set; } = BuiltinAnimationCurve.Linear;
    [Field(7, AllowNull = true, Choices = new[] { "false", "true" }, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringBooleanValueConverter))]
    public bool? Blocking { get; set; } = false;
    [Field(8, AllowNull = true, Choices = new[] { "false", "true" }, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringBooleanValueConverter))]
    public bool? Skippable { get; set; } = false;
    [Field(9, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? BatchId { get; set; }
    [Field(10, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<AnimationLoopMode>))]
    public AnimationLoopMode? LoopMode { get; set; } = AnimationLoopMode.Once;
    [Field(11, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<AnimationBlendMode>))]
    public AnimationBlendMode? BlendMode { get; set; } = AnimationBlendMode.Replace;
}

public sealed class AnimationPropertyValueConverter : JsonConverter<string>, IStringValueEncoding
{
    public IReadOnlyList<string> Choices { get; } = Array.AsReadOnly(Layer.AnimationProperties
        .Concat(ParticleEmitterInstance.AnimationProperties)
        // Shader effect properties come from program metadata, so the compiler cannot enumerate
        // them here. Keep the common Shadertoy time property authorable.
        .Select(property => property.Name).Append("time").Distinct(StringComparer.Ordinal).ToArray());
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

[GenerateFieldSchema]
public partial class TimelineOptions
{
    [Common, Field(0, EditorId = ValueEditorIds.Json)] public JsonElement Timeline { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? BatchId { get; set; }
}

[GenerateFieldSchema]
public partial class AudioPlayOptions
{
    public AudioPlayOptions() => TrackId = "";
    [Common, Field(0, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)] public string TrackId { get; set; }
    [Common, Field(1, AllowNull = true, EditorId = "entry.audioasset")] public string? Asset { get; set; }
    [Field(2, EditorId = ValueEditorIds.Numeric)] public float Gain { get; set; } = 1;
    [Field(3, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(AudioPlacementValueConverter))]
    public AudioPlacement? Placement { get; set; } = AudioPlacement.Enqueue;
    [Field(4, Minimum = 0, EditorId = ValueEditorIds.Numeric)] public float FadeIn { get; set; } = 0;
    [Field(5, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringBooleanValueConverter))]
    public bool? AutoBlocking { get; set; } = false;
}

public sealed class AudioPlacementValueConverter() : StringEnumValueConverter<AudioPlacement>(JsonNamingPolicy.CamelCase);

[GenerateFieldSchema]
public partial class AudioSEffectOptions
{
    public AudioSEffectOptions() => TrackId = "";
    [Common, Field(0, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)] public string TrackId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? InstanceId { get; set; }
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? Id { get; set; }
    [Field(3, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
}

[GenerateFieldSchema]
public partial class VideoPlayOptions
{
    [Common, Field(0, AllowNull = true, EditorId = "entry.videoasset")] public string? Asset { get; set; }
    [Common, Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? HandleId { get; set; } = "video";
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringBooleanValueConverter))]
    public bool? Blocking { get; set; } = true;
    [Field(3, DefaultJson = "[]", EditorId = ValueEditorIds.Json)] public JsonElement SkipPoints { get; set; }
    [Field(4, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(5, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 0;
    [Field(6, EditorId = ValueEditorIds.Numeric)] public float Opacity { get; set; } = 1;
    [Field(7, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? DisplayMode { get; set; } = LayerDisplayMode.Uniform;
}

[GenerateFieldSchema]
public partial class EffectApplyOptions
{
    [Common, Field(0, AllowNull = true, EditorId = "entry.effectprogramasset")] public string? Program { get; set; } = "";
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? Id { get; set; } = "";
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? InstanceId { get; set; }
    [Common, Field(3, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? TargetHandleId { get; set; } = "";
    [Field(4, EditorId = ValueEditorIds.Numeric)] public int Order { get; set; } = 0;
    [Field(5, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
}

[GenerateFieldSchema]
public partial class ParticlePlayOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? InstanceId { get; set; }
    [Field(1, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 100;
    [Field(2, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
}

[GenerateFieldSchema]
public partial class ParticleBurstOptions
{
    [Field(0, Minimum = 1, EditorId = ValueEditorIds.Numeric)] public int Count { get; set; } = 1;
    [Field(1, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 100;
    [Field(2, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
}
