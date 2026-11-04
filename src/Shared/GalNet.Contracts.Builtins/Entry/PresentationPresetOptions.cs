using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class ParallaxTargetDefinition
{
    [Field(0), JsonPropertyName("handleId")] public string HandleId { get; set; } = "";
    [Field(1, DefaultJson = "{}", StructureType = typeof(Scene.LayerTransform), StructureIgnoresCase = true), JsonPropertyName("transform")] public JsonElement Transform { get; set; }
    [Field(2), JsonPropertyName("factor")] public float Factor { get; set; } = 1;
}

public enum MaskMotion { Blink, Open, Close, LetterboxIn, LetterboxOut, Show, Hide }
public enum WeatherKind { Rain, Snow, Petals, Dust, Fireflies }
public enum MoodKind { Memory, Dream, Tension, Injury }
public enum IdleMotion { Breathe, Sway }

[GenerateFieldSchema]
public partial class MaskPresetOptions : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset", ResourceTypeId = "effect-program")] public string Program { get; set; } = BuiltinVisualResources.ShapeMask;
    [Common, Field(7)] public string InstanceId { get; set; } = "mask";
    [Common, Field(8), JsonConverter(typeof(StringEnumValueConverter<MaskMotion>))] public MaskMotion Motion { get; set; }
    [Field(9)] public bool Overlay { get; set; }
    [Field(10, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
    [Field(11)] public float Z { get; set; } = 1000;
    [Field(12, Minimum = 0, Maximum = .8)] public float HoldRatio { get; set; } = .25f;
}

[GenerateFieldSchema]
public partial class FlashPresetOptions : VisualClipOptions
{
    [Field(6)] public string Color { get; set; } = "#FFFFFF";
    [Field(7, Minimum = 0, Maximum = 1)] public float Strength { get; set; } = .8f;
    [Field(8, Minimum = 1, Maximum = 16)] public int Count { get; set; } = 1;
    [Field(9)] public bool Overlay { get; set; }
    [Field(10)] public float Z { get; set; } = 1000;
}

[GenerateFieldSchema]
public partial class IdlePresetOptions : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false)] public string HandleId { get; set; } = "";
    [Field(7), JsonConverter(typeof(StringEnumValueConverter<IdleMotion>))] public IdleMotion Motion { get; set; }
    [Field(8, Minimum = 0, Maximum = .2)] public float ScaleAmount { get; set; } = .008f;
    [Field(9, Minimum = 0, Maximum = 30)] public float RotationDegrees { get; set; } = 1;
    [Field(10, Minimum = 0, Maximum = 100)] public float Distance { get; set; } = 3;
    [Field(11), JsonConverter(typeof(StringEnumValueConverter<VisualPlaybackMode>))] public VisualPlaybackMode PlaybackMode { get; set; } = VisualPlaybackMode.UntilStopped;
}

[GenerateFieldSchema]
public partial class WeatherPresetOptions : VisualClipOptions
{
    [Common, Field(6, AllowNull = true, EditorId = "entry.imageasset", ResourceTypeId = "sprite")] public string? Texture { get; set; }
    [Common, Field(7), JsonConverter(typeof(StringEnumValueConverter<WeatherKind>))] public WeatherKind Weather { get; set; }
    [Field(8, Minimum = 0, Maximum = 1000)] public float Rate { get; set; } = 40;
    [Field(9, Minimum = 1, Maximum = 2000)] public int MaxParticles { get; set; } = 300;
    [Field(10, Minimum = 1, Maximum = 10000)] public float Width { get; set; } = 1920;
    [Field(11, Minimum = 1, Maximum = 10000)] public float Height { get; set; } = 1080;
    [Field(12)] public float Wind { get; set; }
    [Field(13)] public float Z { get; set; } = 100;
    [Field(14)] public int Seed { get; set; } = 20261102;
    [Field(15), JsonConverter(typeof(StringEnumValueConverter<VisualPlaybackMode>))] public VisualPlaybackMode PlaybackMode { get; set; } = VisualPlaybackMode.UntilStopped;
}

[GenerateFieldSchema]
public partial class MoodPresetOptions : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset", ResourceTypeId = "effect-program")] public string Program { get; set; } = BuiltinVisualResources.Mood;
    [Common, Field(7), JsonConverter(typeof(StringEnumValueConverter<MoodKind>))] public MoodKind Mood { get; set; }
    [Field(8)] public string InstanceId { get; set; } = "mood";
    [Field(9, Minimum = 0, Maximum = 1)] public float Strength { get; set; } = .7f;
    [Field(10)] public bool Exit { get; set; }
    [Field(11)] public string TargetHandleId { get; set; } = "";
}

[GenerateFieldSchema]
public partial class HeartbeatPresetOptions : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset", ResourceTypeId = "effect-program")] public string Program { get; set; } = BuiltinVisualResources.Vignette;
    [Field(7)] public string HandleId { get; set; } = "";
    [Field(8, Minimum = 0, Maximum = 1)] public float Strength { get; set; } = .65f;
    [Field(9), JsonConverter(typeof(StringEnumValueConverter<VisualPlaybackMode>))] public VisualPlaybackMode PlaybackMode { get; set; } = VisualPlaybackMode.UntilStopped;
    [Field(10, AllowNull = true, EditorId = "entry.audioasset", ResourceTypeId = "audio")] public string? Sound { get; set; }
    [Field(11)] public string TrackId { get; set; } = "sfx";
}

[GenerateFieldSchema]
public partial class ParallaxPresetOptions : VisualClipOptions
{
    // Each target explicitly supplies handleId, transform and factor.
    [Common, Field(6, DefaultJson = "[]", EditorId = ValueEditorIds.Json, StructureType = typeof(ParallaxTargetDefinition[]), StructureUsesFieldIds = true)] public JsonElement Targets { get; set; }
    [Field(7)] public float DistanceX { get; set; } = 80;
    [Field(8)] public float DistanceY { get; set; }
    [Field(9, Minimum = .01, Maximum = 10)] public float Scale { get; set; } = 1.05f;
    [Field(10)] public float AnchorX { get; set; }
    [Field(11)] public float AnchorY { get; set; }
}

[GenerateFieldSchema]
public partial class EmphasisPresetOptions : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset", ResourceTypeId = "effect-program")] public string Program { get; set; } = BuiltinVisualResources.ColorGrade;
    [Field(7)] public string InstanceId { get; set; } = "speaker-emphasis";
    [Common, Field(8, DefaultJson = "[]", EditorId = ValueEditorIds.Json, StructureType = typeof(string[]))] public JsonElement HandleIds { get; set; }
    [Common, Field(9)] public string ActiveHandleId { get; set; } = "";
    [Field(10, Minimum = -8, Maximum = 0)] public float DimExposure { get; set; } = -1;
    [Field(11)] public bool Clear { get; set; }
    [Field(12), CharacterReference] public string Speaker { get; set; } = "";
    [Common, Field(13, EditorId = "entry.multilinetext"), LocalizedText] public string Content { get; set; } = "";
    [Field(14, AllowNull = true, EditorId = "entry.audioasset", ResourceTypeId = "audio")] public string? Voice { get; set; }
    [Field(15)] public string TrackId { get; set; } = "vocal";
}

[GenerateFieldSchema]
public partial class StylizedTransitionOptions : CrossFadeOptionsBase
{
    [Common, Field(10, AllowNull = true, EditorId = "entry.effectprogramasset", ResourceTypeId = "effect-program")] public string? Program { get; set; }
    [Field(11, Minimum = .01, Maximum = 8)] public float Strength { get; set; } = 1;
    [Field(12)] public float AnchorX { get; set; }
    [Field(13)] public float AnchorY { get; set; }
}
