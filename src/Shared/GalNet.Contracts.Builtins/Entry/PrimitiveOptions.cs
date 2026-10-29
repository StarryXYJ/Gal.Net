using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class AudioTrackOptions
{
    public AudioTrackOptions() => TrackId = "";
    [Common, Field(0, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)]
    public string TrackId { get; set; }
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
    public AudioStopOptions() => TrackId = "";
    [Common, Field(0, Required = true, AllowNull = false, EditorId = ValueEditorIds.Text)]
    public string TrackId { get; set; }
    [Field(1, AllowNull = true, EditorId = ValueEditorIds.Choice), JsonConverter(typeof(StringBooleanValueConverter))]
    public bool? ClearQueue { get; set; } = true;
    [Field(2, AllowNull = true, Minimum = 0, EditorId = ValueEditorIds.Numeric)]
    public float? FadeOut { get; set; }
}
