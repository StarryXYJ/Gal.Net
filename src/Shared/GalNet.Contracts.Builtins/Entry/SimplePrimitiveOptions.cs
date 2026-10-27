using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class DialogueTextOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.autocomplete"), CharacterReference]
    [Common]
    public string? Speaker { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.multilinetext"), LocalizedText]
    [Common]
    public string? Content { get; set; }
}

[GenerateFieldSchema]
public partial class NvlAppendOptions
{
    [Field(0, AllowNull = true, EditorId = "entry.multilinetext"), LocalizedText]
    [Common]
    public string? Content { get; set; }
}

[GenerateFieldSchema]
public partial class LayerHandleOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    [Common]
    public string? HandleId { get; set; }
}

[GenerateFieldSchema]
public partial class LayerReplaceOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    [Common]
    public string? HandleId { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.imageasset")]
    [Common]
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
    public AudioSEffectStopOptions() => TargetId = "";
    [Field(1, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)]
    [Common]
    public string TargetId { get; set; }
    [Common, Field(2, AllowNull = true, EditorId = ValueEditorIds.Choice)]
    public string? InstanceId { get; set; }
    [Common, Field(0, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(AudioTargetScopeValueConverter))] public GalNet.Contracts.Builtins.Audio.SEffectScope Scope { get; set; } = GalNet.Contracts.Builtins.Audio.SEffectScope.Track;
    [Field(3, EditorId = ValueEditorIds.Choice)] public bool All { get; set; }
}

[GenerateFieldSchema]
public partial class AudioSEffectUpdateOptions
{
    [Common, Field(0, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(AudioTargetScopeValueConverter))]
    public GalNet.Contracts.Builtins.Audio.SEffectScope Scope { get; set; } = GalNet.Contracts.Builtins.Audio.SEffectScope.Track;
    [Common, Field(1, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)] public string TargetId { get; set; } = "";
    [Common, Field(2, AllowNull = true, EditorId = ValueEditorIds.Text)] public string? InstanceId { get; set; }
    [Common, Field(3, DefaultJson = "{}", EditorId = ValueEditorIds.Json)] public JsonElement Parameters { get; set; }
}

[GenerateFieldSchema]
public partial class VideoStopOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    [Common]
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
    [Common]
    public string? Target { get; set; }
    [Field(1, AllowNull = true, EditorId = "entry.expression")]
    [Common]
    public string? Expression { get; set; }
}

[GenerateFieldSchema]
public partial class GalleryUnlockOptions
{
    [Field(0, EditorId = ValueEditorIds.Numeric)]
    public int Id { get; set; }
}
