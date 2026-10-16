using GalNet.Core.Parameters;

namespace GeneralTest.Editor;

[GenerateFieldSchema]
public partial class EditorWaitOptions
{
    [Field(0, LabelKey = "Entry.Parameter.duration")]
    public float Duration { get; set; } = 1;

    [Field(1, AllowNull = false)]
    public string? Label { get; set; }
}
