using System.Text.Json.Serialization;
using GalNet.Core.Parameters;
using GalNet.Core.Scene;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class DialogueTextOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.autocomplete"), CharacterReference]
    public string? Speaker { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.multilinetext"), LocalizedText]
    public string? Content { get; set; }
}

[GenerateFieldSchema]
public partial class NvlAppendOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.multilinetext"), LocalizedText]
    public string? Content { get; set; }
}

[GenerateFieldSchema]
public partial class LayerHandleOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? HandleId { get; set; }
}

[GenerateFieldSchema]
public partial class LayerReplaceOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? HandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.imageasset")]
    public string? AssetId { get; set; }
}

[GenerateFieldSchema]
public partial class InstanceIdOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? InstanceId { get; set; }
}

[GenerateFieldSchema]
public partial class AudioSEffectStopOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? TrackId { get; set; } = "bgm";
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? InstanceId { get; set; }
}

[GenerateFieldSchema]
public partial class VideoStopOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? HandleId { get; set; } = "video";
}

[GenerateFieldSchema]
public partial class AnimationStopOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? PlaybackHandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringEnumValueConverter<AnimationStopMode>))]
    public AnimationStopMode? Mode { get; set; } = AnimationStopMode.AfterIteration;
}

[GenerateFieldSchema]
public partial class VariableSetOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.variablename")]
    public string? Target { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.expression")]
    public string? Expression { get; set; }
}

[GenerateFieldSchema]
public partial class GalleryUnlockOptions
{
    [Field(0, EditorId = ValueEditorIds.Numeric)]
    public int Id { get; set; }
}
