using GalNet.Core.Entry;

namespace GeneralTest.Entry;

public class EntryModelTests
{
    [Test]
    public void Registry_Should_Create_All_BuiltIn_Entries()
    {
        Assert.That(EntryRegistry.Definitions, Has.Count.EqualTo(18));
        foreach (var definition in EntryRegistry.Definitions)
            Assert.That(EntryRegistry.Create(definition.Type).Type, Is.EqualTo(definition.Type));
    }

    [Test]
    public void Create_Should_Apply_Defaults_And_Discard_Unknown_Values()
    {
        var entry = EntryRegistry.Create(ShowLayerEntry.TypeId, 3, "flag", new Dictionary<string, string>
        {
            ["id"] = "hero", ["unknown"] = "discard"
        });

        Assert.That(entry, Is.TypeOf<ShowLayerEntry>());
        Assert.That(entry.Id, Is.EqualTo(3));
        Assert.That(entry.Condition, Is.EqualTo("flag"));
        Assert.That(entry.Values["duration"], Is.EqualTo("0.5"));
        Assert.That(entry.Values["id"], Is.EqualTo("hero"));
        Assert.That(entry.Values, Does.Not.ContainKey("unknown"));
    }

    [Test]
    public void Entry_Values_Should_Not_Be_Shared()
    {
        var first = EntryRegistry.Create(TextEntry.TypeId);
        var second = EntryRegistry.Create(TextEntry.TypeId);
        first.Values["content"] = "first";
        Assert.That(second.Values, Does.Not.ContainKey("content"));
    }

    [Test]
    public void Unknown_Type_Should_Throw_Clear_Error() =>
        Assert.That(() => EntryRegistry.Create("jump"), Throws.TypeOf<InvalidDataException>().With.Message.Contains("jump"));

    [TestCase("control.show")]
    [TestCase("control.hide")]
    [TestCase("control.set")]
    [TestCase("variable.eval")]
    public void Removed_Types_Should_Be_Unknown(string type) =>
        Assert.That(() => EntryRegistry.Create(type), Throws.TypeOf<InvalidDataException>());

    [Test]
    public void Concrete_Action_Types_Should_Not_Declare_Action_Parameter() =>
        Assert.That(EntryRegistry.Definitions.All(x => !x.Parameters.ContainsKey("action")), Is.True);

    [Test]
    public void TextEntry_Should_Not_Expose_Widget_Parameter()
    {
        var definition = EntryRegistry.Get(TextEntry.TypeId);
        Assert.That(definition.Parameters.Keys, Is.EquivalentTo(new[] { "speaker", "content", "voice" }));
        Assert.That(EntryRegistry.Create(TextEntry.TypeId).Values, Does.Not.ContainKey("widget"));
    }

    [Test]
    public void Dialogue_Visibility_Entries_Should_Have_No_Parameters()
    {
        Assert.That(EntryRegistry.Get(ShowDialogueEntry.TypeId).Parameters, Is.Empty);
        Assert.That(EntryRegistry.Get(HideDialogueEntry.TypeId).Parameters, Is.Empty);
    }

    [Test]
    public void SetVariable_Should_Only_Expose_Target_And_Expression()
    {
        var definition = EntryRegistry.Get(SetVariableEntry.TypeId);
        Assert.That(definition.Parameters.Keys, Is.EquivalentTo(new[] { "target", "expression" }));
        Assert.That(definition.Parameters["target"], Is.EqualTo(EntryParameterType.VariableName));
        Assert.That(definition.Parameters["expression"], Is.EqualTo(EntryParameterType.Expression));
    }

    [Test]
    public void Legacy_SetVariable_Parameters_Should_Fail_Loading() =>
        Assert.That(() => EntryRegistry.Create(SetVariableEntry.TypeId, values: new Dictionary<string, string>
        {
            ["target"] = "flag",
            ["value"] = "true",
            ["valueType"] = "bool"
        }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("target").And.Message.Contains("expression"));

    [Test]
    public void Every_Definition_Should_Have_A_Category() =>
        Assert.That(EntryRegistry.Definitions.All(x => !string.IsNullOrWhiteSpace(x.Category)), Is.True);
}
