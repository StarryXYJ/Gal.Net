using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class AudioTrackOptions
{
    [Field(0, AllowNull = true, EditorId = ValueEditorIds.Text)]
    public string? TrackId { get; set; } = "bgm";
}

[GenerateFieldSchema]
public partial class WaitOptions
{
    [Field(0, Minimum = 0, EditorId = ValueEditorIds.Numeric)]
    public float Duration { get; set; } = 1;
}
