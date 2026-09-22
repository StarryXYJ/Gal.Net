using GalNet.Core.Entry;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Entry;

public class EntryModelTests
{
    private static TargetProfileEntryCatalog Catalog => BuiltinEntryModules.CreateRecommendedTargetProfile();

    [Test]
    public void Registry_Should_Create_All_BuiltIn_Entries()
    {
        Assert.That(Catalog.Definitions, Has.Count.EqualTo(31));
        foreach (var definition in Catalog.Definitions)
            Assert.That(Catalog.Create(definition.Type).Type, Is.EqualTo(definition.Type));
    }

    [Test]
    public void Create_Should_Apply_Defaults_And_Discard_Unknown_Values()
    {
        var entry = Catalog.Create(ShowLayerEntry.TypeId, 3, "flag", new Dictionary<string, string>
        {
            ["handleId"] = "hero", ["unknown"] = "discard"
        });

        Assert.That(entry, Is.TypeOf<AuthoringPrimitiveEntry>());
        Assert.That(entry.Id, Is.EqualTo(3));
        Assert.That(entry.Condition, Is.EqualTo("flag"));
        Assert.That(entry.Values["handleId"], Is.EqualTo("hero"));
        Assert.That(entry.Values, Does.Not.ContainKey("unknown"));
        Assert.That(entry.Values, Does.Not.ContainKey("transitionDuration"));
    }

    [Test]
    public void Entry_Values_Should_Not_Be_Shared()
    {
        var first = Catalog.Create(TextEntry.TypeId);
        var second = Catalog.Create(TextEntry.TypeId);
        first.Values["content"] = "first";
        Assert.That(second.Values, Does.Not.ContainKey("content"));
    }

    [Test]
    public void Unknown_Type_Should_Throw_Clear_Error() =>
        Assert.That(() => Catalog.Create("jump"), Throws.TypeOf<InvalidDataException>().With.Message.Contains("jump"));

    [TestCase("control.show")]
    [TestCase("control.hide")]
    [TestCase("control.set")]
    [TestCase("variable.eval")]
    public void Removed_Types_Should_Be_Unknown(string type) =>
        Assert.That(() => Catalog.Create(type), Throws.TypeOf<InvalidDataException>());

    [Test]
    public void Concrete_Action_Types_Should_Not_Declare_Action_Parameter() =>
        Assert.That(Catalog.Definitions.All(x => !x.Parameters.ContainsKey("action")), Is.True);

    [Test]
    public void TextEntry_Should_Not_Expose_Widget_Parameter()
    {
        var definition = Catalog.Get(TextEntry.TypeId);
        Assert.That(definition.Parameters.Keys, Is.EquivalentTo(new[] { "speaker", "content", "voice" }));
        Assert.That(Catalog.Create(TextEntry.TypeId).Values, Does.Not.ContainKey("widget"));
    }

    [Test]
    public void Dialogue_Visibility_Entries_Should_Have_No_Parameters()
    {
        Assert.That(Catalog.Get(ShowDialogueEntry.TypeId).Parameters, Is.Empty);
        Assert.That(Catalog.Get(HideDialogueEntry.TypeId).Parameters, Is.Empty);
    }

    [Test]
    public void SetVariable_Should_Only_Expose_Target_And_Expression()
    {
        var definition = Catalog.Get(SetVariableEntry.TypeId);
        Assert.That(definition.Parameters.Keys, Is.EquivalentTo(new[] { "target", "expression" }));
        Assert.That(definition.Parameters["target"], Is.EqualTo(EntryParameterType.VariableName));
        Assert.That(definition.Parameters["expression"], Is.EqualTo(EntryParameterType.Expression));
    }

    [Test]
    public void Every_Definition_Should_Have_A_Category() =>
        Assert.That(Catalog.Definitions.All(x => !string.IsNullOrWhiteSpace(x.Category)), Is.True);

    [Test]
    public void Color_Field_Transitions_Are_Composite_Entries_With_Declared_Parameter_Schemas()
    {
        foreach (var type in new[] { BlackFadeTransitionEntry.TypeId, WhiteFadeTransitionEntry.TypeId, ColorFadeTransitionEntry.TypeId })
        {
            var definition = Catalog.Get(type);
            Assert.That(definition.Kind, Is.EqualTo(EntryKind.Composite));
            Assert.That(definition.Parameters.Keys, Does.Contain("fromLayerHandleId"));
            Assert.That(definition.Parameters.Keys, Does.Contain("toLayerHandleId"));
            Assert.That(definition.Parameters.Keys, Does.Contain("fadeInDuration"));
            Assert.That(definition.Parameters.Keys, Does.Contain("holdDuration"));
            Assert.That(definition.Parameters.Keys, Does.Contain("fadeOutDuration"));
        }
        Assert.That(Catalog.Get(ColorFadeTransitionEntry.TypeId).Parameters.Keys, Does.Contain("color"));
    }

    [Test]
    public void Slide_Transition_Is_A_Composite_Entry_With_Direction_And_Distance()
    {
        var definition = Catalog.Get(SlideTransitionEntry.TypeId);
        Assert.That(definition.Kind, Is.EqualTo(EntryKind.Composite));
        Assert.That(definition.Parameters["direction"], Is.EqualTo(EntryParameterType.Select));
        Assert.That(definition.Parameters["distance"], Is.EqualTo(EntryParameterType.Float));
        Assert.That(definition.Options["direction"], Is.EquivalentTo(new[] { "Left", "Right", "Up", "Down" }));
    }

    [Test]
    public void Blinds_Transition_Is_A_Composite_Entry_With_Mask_Parameters()
    {
        var definition = Catalog.Get(BlindsTransitionEntry.TypeId);
        Assert.That(definition.Kind, Is.EqualTo(EntryKind.Composite));
        Assert.That(definition.Parameters["maskProgram"], Is.EqualTo(EntryParameterType.EffectProgramAsset));
        Assert.That(definition.Parameters["bladeCount"], Is.EqualTo(EntryParameterType.Integer));
        Assert.That(definition.Parameters["orientation"], Is.EqualTo(EntryParameterType.Select));
        Assert.That(definition.Options["orientation"], Is.EquivalentTo(new[] { "Vertical", "Horizontal" }));
    }

    [Test]
    public void Effect_apply_uses_a_typed_program_resource()
    {
        var definition = Catalog.Get(ApplyEffectEntry.TypeId);
        Assert.That(definition.Parameters["program"], Is.EqualTo(EntryParameterType.EffectProgramAsset));
    }
}
