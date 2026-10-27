using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

public enum RevealMode { Wipe, Radial, Noise, Blinds, Mask }
public enum MotionPreset { FadeIn, FadeOut, SlideIn, SlideOut, PopIn, Jump, Nod, ShakeHead, Push }
public enum VisualPlaybackMode { Timed, UntilStopped }

[GenerateFieldSchema]
public partial class RevealTransitionOptions : CrossFadeOptionsBase
{
    [Common, Field(10, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset")] public string Program { get; set; } = "";
    [Field(11, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<RevealMode>))] public RevealMode Mode { get; set; }
    [Field(12, Minimum = 0.001, Maximum = 1)] public float Softness { get; set; } = .05f;
    [Field(13)] public float Angle { get; set; }
    [Field(14, DefaultJson = "[0.5,0.5]", EditorId = ValueEditorIds.Json)] public JsonElement Center { get; set; }
    [Field(15, Minimum = 1, Maximum = 256)] public float Count { get; set; } = 12;
    [Field(16, AllowNull = true, EditorId = "entry.imageasset")] public string? MaskAsset { get; set; }
}

[GenerateFieldSchema]
public partial class BlurTransitionOptions : CrossFadeOptionsBase
{
    [Common, Field(10, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset")] public string BlurProgram { get; set; } = "";
    [Field(11, Minimum = 0, Maximum = 64)] public float Radius { get; set; } = 24;
}

public abstract class VisualClipOptions
{
    [Field(0, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)] public string PlaybackHandleId { get; set; } = "";
    [Field(1, Minimum = 1, Maximum = 240)] public int FrameRate { get; set; } = 60;
    [Common, Field(2, Minimum = 2, Maximum = 36000)] public int DurationFrames { get; set; } = 36;
    [Common, Field(3)] public bool Blocking { get; set; } = true;
    [Field(4)] public bool Skippable { get; set; } = true;
    [Field(5, AllowNull = true)] public string? BatchId { get; set; }
}

[GenerateFieldSchema]
public partial class MotionPresetOptions : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false)] public string HandleId { get; set; } = "";
    [Common, Field(7, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<MotionPreset>))] public MotionPreset Motion { get; set; }
    [Field(8, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(9, Minimum = 0, Maximum = 100000)] public float Distance { get; set; } = 120;
    [Field(10)] public float Angle { get; set; }
    [Field(11, Minimum = 0.01, Maximum = 100)] public float Scale { get; set; } = 1.15f;
    [Field(12)] public float AnchorX { get; set; }
    [Field(13)] public float AnchorY { get; set; }
    [Field(14, Minimum = -180, Maximum = 180)] public float RotationDegrees { get; set; } = 8;
}

[GenerateFieldSchema]
public partial class ShakePresetOptions : VisualClipOptions
{
    [Common, Field(10, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<VisualPlaybackMode>))] public VisualPlaybackMode PlaybackMode { get; set; }
    [Common, Field(6, DefaultJson = "[]", EditorId = ValueEditorIds.Json)] public JsonElement Targets { get; set; }
    [Field(7, Minimum = 0, Maximum = 10000)] public float Amplitude { get; set; } = 16;
    [Field(8, Minimum = 1, Maximum = 30)] public int Cycles { get; set; } = 4;
    [Field(9)] public float Angle { get; set; }
}

public abstract class SpeedLinesPresetOptionsBase : VisualClipOptions
{
    [Common, Field(6, Required = true, AllowNull = false, EditorId = "entry.effectprogramasset")] public string Program { get; set; } = "";
    [Field(7, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
    [Field(8)] public float Z { get; set; } = 900;
    [Field(9, Minimum = -20, Maximum = 20)] public float Speed { get; set; } = 2;
    [Field(10, Minimum = 0, Maximum = 1)] public float Flicker { get; set; } = .3f;
}

[GenerateFieldSchema]
public partial class SpeedLinesPresetOptions : SpeedLinesPresetOptionsBase
{
    [Common, Field(11, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<VisualPlaybackMode>))] public VisualPlaybackMode PlaybackMode { get; set; }
}

[GenerateFieldSchema]
public partial class ImpactPresetOptions : SpeedLinesPresetOptionsBase
{
    [Common, Field(11, DefaultJson = "[]", EditorId = ValueEditorIds.Json)] public JsonElement Targets { get; set; }
    [Field(12, Minimum = 0, Maximum = 10000)] public float Amplitude { get; set; } = 16;
    [Field(13, Minimum = 1, Maximum = 30)] public int Cycles { get; set; } = 4;
    [Field(14)] public float Angle { get; set; }
    [Field(15, Minimum = 0, Maximum = 1)] public float FlashOpacity { get; set; } = .7f;
}
