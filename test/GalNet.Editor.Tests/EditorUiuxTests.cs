using GalNet.Core.Parameters;

namespace GeneralTest.Editor;

public sealed class EditorUiuxTests
{
    [Test]
    public void CommonAnnotationFlowsThroughInheritedGeneratedSchema()
    {
        Assert.That(CommonDerivedOptions.Schema.Single(field => field.Id == "title").Editor!.IsCommon, Is.True);
        Assert.That(CommonDerivedOptions.Schema.Single(field => field.Id == "advanced").Editor!.IsCommon, Is.False);
        Assert.That(new EditorHints(IsCommon: true).IsCommon, Is.True);
    }
}

public abstract class CommonBaseOptions
{
    [Field(0), Common] public string? Title { get; set; }
}

[GenerateFieldSchema]
public partial class CommonDerivedOptions : CommonBaseOptions
{
    [Field(1)] public float Advanced { get; set; }
}
