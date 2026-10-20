using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;
using GalNet.Core.Scene;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class CrossFadeOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? PlaybackHandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? OldHandleId { get; set; }
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? NewHandleId { get; set; }
    [Field(3, AllowNull = true, EditorId = "entry.imageasset")] public string? AssetId { get; set; }
    [Field(4, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(5, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 0;
    [Field(6, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? DisplayMode { get; set; } = LayerDisplayMode.Fill;
    [Field(7, EditorId = ValueEditorIds.Numeric)] public int FrameRate { get; set; } = 60;
    [Field(8, EditorId = ValueEditorIds.Numeric)] public int DurationFrames { get; set; } = 48;
    [Field(9, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? BatchId { get; set; }
}

public enum SlideDirection { Left, Right, Up, Down }
public enum BlindsOrientation { Vertical, Horizontal }

[GenerateFieldSchema]
public partial class SlideOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? PlaybackHandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? FromLayerHandleId { get; set; }
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? ToLayerHandleId { get; set; }
    [Field(3, AllowNull = true, EditorId = "entry.imageasset")] public string? ToAssetId { get; set; }
    [Field(4, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement ToTransform { get; set; }
    [Field(5, EditorId = ValueEditorIds.Numeric)] public float ToZ { get; set; } = 0;
    [Field(6, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? ToDisplayMode { get; set; } = LayerDisplayMode.Fill;
    [Field(7, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<SlideDirection>))]
    public SlideDirection? Direction { get; set; } = SlideDirection.Left;
    [Field(8, EditorId = ValueEditorIds.Numeric)] public float Distance { get; set; } = 1920;
    [Field(9, EditorId = ValueEditorIds.Numeric)] public int FrameRate { get; set; } = 60;
    [Field(10, EditorId = ValueEditorIds.Numeric)] public int DurationFrames { get; set; } = 30;
    [Field(11, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? BatchId { get; set; }
}

[GenerateFieldSchema]
public partial class BlindsOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? PlaybackHandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? OldHandleId { get; set; }
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? NewHandleId { get; set; }
    [Field(3, AllowNull = true, EditorId = "entry.imageasset")] public string? AssetId { get; set; }
    [Field(4, AllowNull = true, EditorId = "entry.effectprogramasset")] public string? MaskProgram { get; set; }
    [Field(5, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Transform { get; set; }
    [Field(6, EditorId = ValueEditorIds.Numeric)] public float Z { get; set; } = 0;
    [Field(7, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? DisplayMode { get; set; } = LayerDisplayMode.Fill;
    [Field(8, EditorId = ValueEditorIds.Numeric)] public int BladeCount { get; set; } = 12;
    [Field(9, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<BlindsOrientation>))]
    public BlindsOrientation? Orientation { get; set; } = BlindsOrientation.Vertical;
    [Field(10, EditorId = ValueEditorIds.Numeric)] public int FrameRate { get; set; } = 60;
    [Field(11, EditorId = ValueEditorIds.Numeric)] public int DurationFrames { get; set; } = 48;
    [Field(12, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? BatchId { get; set; }
}

public abstract class ColorFieldOptionsBase
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? PlaybackHandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? FromLayerHandleId { get; set; }
    [Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? ToLayerHandleId { get; set; }
    [Field(3, AllowNull = true, EditorId = "entry.imageasset")] public string? ToAssetId { get; set; }
    [Field(4, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement ToTransform { get; set; }
    [Field(5, EditorId = ValueEditorIds.Numeric)] public float ToZ { get; set; } = 0;
    [Field(6, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<LayerDisplayMode>))]
    public LayerDisplayMode? ToDisplayMode { get; set; } = LayerDisplayMode.Fill;
    [Field(7, EditorId = ValueEditorIds.Numeric)] public float OverlayZ { get; set; } = 1000;
    [Field(8, EditorId = ValueEditorIds.Numeric)] public float FadeInDuration { get; set; } = 0.4f;
    [Field(9, EditorId = ValueEditorIds.Numeric)] public float HoldDuration { get; set; } = 0.1f;
    [Field(10, EditorId = ValueEditorIds.Numeric)] public float FadeOutDuration { get; set; } = 0.4f;
    [Field(11, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? BatchId { get; set; }
}

[GenerateFieldSchema]
public partial class ColorFieldOptions : ColorFieldOptionsBase;

[GenerateFieldSchema]
public partial class ColorFadeOptions : ColorFieldOptionsBase
{
    [Field(12, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? Color { get; set; } = "#000000";
}

[GenerateFieldSchema]
public partial class TextVoiceOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.autocomplete"), CharacterReference] public string? Speaker { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.multilinetext"), LocalizedText] public string? Content { get; set; }
    [Field(2, AllowNull = true, EditorId = "entry.audioasset")] public string? Asset { get; set; }
    [Field(3, EditorId = ValueEditorIds.Numeric)] public float Gain { get; set; } = 1;
    [Field(4, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? TrackId { get; set; } = "vocal";
}

[GenerateFieldSchema]
public partial class NvlVoiceOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.multilinetext"), LocalizedText] public string? Content { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.audioasset")] public string? Asset { get; set; }
    [Field(2, EditorId = ValueEditorIds.Numeric)] public float Gain { get; set; } = 1;
    [Field(3, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? TrackId { get; set; } = "vocal";
}
