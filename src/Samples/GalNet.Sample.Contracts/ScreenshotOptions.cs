using GalNet.Core.Parameters;

namespace GalNet.Project.Contracts;

[GenerateFieldSchema]
public partial class ScreenshotOptions
{
    [Field(0, AllowNull = false, MaxLength = 120, ValueFormat = "png-file-name", LabelKey = "Entry.Parameter.fileName")]
    public string? FileName { get; set; }
}
