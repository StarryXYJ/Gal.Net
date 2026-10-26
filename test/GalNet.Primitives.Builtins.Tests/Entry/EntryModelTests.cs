using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Primitives.Builtins;
using System.Text.Json;

namespace GeneralTest.Entry;

public class EntryModelTests
{
    private static readonly string[] TextParameters = ["speaker", "content"];
    private static readonly string[] VariableParameters = ["target", "expression"];
    private static readonly string[] SlideDirections = ["Left", "Right", "Up", "Down"];
    private static readonly string[] BlindOrientations = ["Vertical", "Horizontal"];

    private static TargetProfileEntryCatalog Catalog => new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());

    [Test]
    public void Registry_Should_Create_All_BuiltIn_Entries()
    {
        Assert.That(Catalog.Definitions, Has.Count.EqualTo(45));
        foreach (var definition in Catalog.Definitions)
            Assert.That(Catalog.Create(definition.Type).Type, Is.EqualTo(definition.Type));
    }

    [Test]
    public void AuthoringCreatePreservesJsonAndUnknownDraftFields()
    {
        var entry = Catalog.Create(BuiltinEntryIds.ShowLayer, 3, "flag", JsonSerializer.SerializeToElement(new Dictionary<string, string>
        {
            ["handleId"] = "hero",
            ["unknown"] = "discard"
        }));

        Assert.That(entry, Is.TypeOf<AuthoringPrimitiveEntry>());
        Assert.That(entry.Id, Is.EqualTo(3));
        Assert.That(entry.Condition, Is.EqualTo("flag"));
        Assert.That(entry.Arguments.GetProperty("handleId").GetString(), Is.EqualTo("hero"));
        Assert.That(entry.Arguments.GetProperty("unknown").GetString(), Is.EqualTo("discard"));
        Assert.That(entry.Arguments.TryGetProperty("transitionDuration", out _), Is.False);
    }

    [Test]
    public void Entry_Values_Should_Not_Be_Shared()
    {
        var first = Catalog.Create(BuiltinEntryIds.Text);
        var second = Catalog.Create(BuiltinEntryIds.Text);
        first.SetArguments(JsonSerializer.SerializeToElement(new { content = "first" }));
        Assert.That(second.Arguments.TryGetProperty("content", out _), Is.False);
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
        Assert.That(Catalog.Definitions.Where(x => x.Type != BuiltinEntryIds.InvokeCustom)
            .All(x => !x.DynamicParameters.ContainsKey("action")), Is.True);

    [Test]
    public void TextEntry_Should_Not_Expose_Widget_Parameter()
    {
        var definition = Catalog.Get(BuiltinEntryIds.Text);
        Assert.That(definition.DynamicParameters.Keys, Is.EquivalentTo(TextParameters));
        Assert.That(Catalog.Create(BuiltinEntryIds.Text).Arguments.TryGetProperty("widget", out _), Is.False);
    }

    [Test]
    public void Dialogue_Visibility_Entries_Should_Have_No_Parameters()
    {
        Assert.That(Catalog.Get(BuiltinEntryIds.ShowDialogue).DynamicParameters, Is.Empty);
        Assert.That(Catalog.Get(BuiltinEntryIds.HideDialogue).DynamicParameters, Is.Empty);
    }

    [Test]
    public void Nvl_Entries_Expose_Explicit_Visibility_Clear_And_Append_Schemas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Catalog.Get(BuiltinEntryIds.ShowNvl).DynamicParameters, Is.Empty);
            Assert.That(Catalog.Get(BuiltinEntryIds.HideNvl).DynamicParameters, Is.Empty);
            Assert.That(Catalog.Get(BuiltinEntryIds.ClearNvl).DynamicParameters, Is.Empty);
            Assert.That(Catalog.Get(BuiltinEntryIds.AppendNvl).DynamicParameters.Keys, Is.EquivalentTo(["content"]));
        });
    }

    [Test]
    public void SetVariable_Should_Only_Expose_Target_And_Expression()
    {
        var definition = Catalog.Get(BuiltinEntryIds.SetVariable);
        Assert.That(definition.DynamicParameters.Keys, Is.EquivalentTo(VariableParameters));
        Assert.That(definition.DynamicParameters["target"].Editor!.EditorId, Is.EqualTo("entry.variablename"));
        Assert.That(definition.DynamicParameters["expression"].Editor!.EditorId, Is.EqualTo("entry.expression"));
    }

    [Test]
    public void Every_Definition_Should_Have_A_Category() =>
        Assert.That(Catalog.Definitions.All(x => !string.IsNullOrWhiteSpace(x.Category)), Is.True);

    [Test]
    public void Color_Field_Transitions_Are_Composite_Entries_With_Declared_Parameter_Schemas()
    {
        foreach (var type in new[] { BuiltinEntryIds.BlackFadeTransition, BuiltinEntryIds.WhiteFadeTransition, BuiltinEntryIds.ColorFadeTransition })
        {
            var definition = Catalog.Get(type);
            Assert.That(definition.Kind, Is.EqualTo(EntryKind.Composite));
            Assert.That(definition.DynamicParameters.Keys, Does.Contain("fromLayerHandleId"));
            Assert.That(definition.DynamicParameters.Keys, Does.Contain("toLayerHandleId"));
            Assert.That(definition.DynamicParameters.Keys, Does.Contain("fadeInDuration"));
            Assert.That(definition.DynamicParameters.Keys, Does.Contain("holdDuration"));
            Assert.That(definition.DynamicParameters.Keys, Does.Contain("fadeOutDuration"));
        }
        Assert.That(Catalog.Get(BuiltinEntryIds.ColorFadeTransition).DynamicParameters.Keys, Does.Contain("color"));
    }

    [Test]
    public void Slide_Transition_Is_A_Composite_Entry_With_Direction_And_Distance()
    {
        var definition = Catalog.Get(BuiltinEntryIds.SlideTransition);
        Assert.That(definition.Kind, Is.EqualTo(EntryKind.Composite));
        Assert.That(definition.DynamicParameters["direction"].Editor!.EditorId, Is.EqualTo("choice"));
        Assert.That(definition.DynamicParameters["distance"].ValueType, Is.EqualTo(typeof(float)));
        Assert.That(definition.DynamicParameters["direction"].Constraints!.Value.GetProperty("enum").EnumerateArray().Select(value => value.GetString()), Is.EquivalentTo(SlideDirections));
    }

    [Test]
    public void Blinds_Transition_Is_A_Composite_Entry_With_Mask_Parameters()
    {
        var definition = Catalog.Get(BuiltinEntryIds.BlindsTransition);
        Assert.That(definition.Kind, Is.EqualTo(EntryKind.Composite));
        Assert.That(definition.DynamicParameters["maskProgram"].Editor!.EditorId, Is.EqualTo("entry.effectprogramasset"));
        Assert.That(definition.DynamicParameters["bladeCount"].ValueType, Is.EqualTo(typeof(int)));
        Assert.That(definition.DynamicParameters["orientation"].Editor!.EditorId, Is.EqualTo("choice"));
        Assert.That(definition.DynamicParameters["orientation"].Constraints!.Value.GetProperty("enum").EnumerateArray().Select(value => value.GetString()), Is.EquivalentTo(BlindOrientations));
    }

    [Test]
    public void Effect_apply_uses_a_typed_program_resource()
    {
        var definition = Catalog.Get(BuiltinEntryIds.ApplyEffect);
        Assert.That(definition.DynamicParameters["program"].Editor!.EditorId, Is.EqualTo("entry.effectprogramasset"));
    }
}
