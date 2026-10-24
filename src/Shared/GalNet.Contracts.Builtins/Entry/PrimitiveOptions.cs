using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class AudioTrackOptions
{
    [Common, Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? TrackId { get; set; } = "bgm";
}

[GenerateFieldSchema]
public partial class WaitOptions
{
    [Common, Field(0, Minimum = 0, EditorId = ValueEditorIds.Numeric)]
    public float Duration { get; set; } = 1;
}

[GenerateFieldSchema]
public partial class AudioStopOptions
{
    [Common, Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? TrackId { get; set; } = "bgm";
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringBooleanValueConverter))]
    public bool? ClearQueue { get; set; } = true;
    [Field(2, Minimum = 0, EditorId = ValueEditorIds.Numeric)]
    public float FadeOut { get; set; } = 0;
}
