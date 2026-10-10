using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Editor;

public class GraphEditingServiceTests
{
    private static readonly int[] ThreeEntryIds = [1, 2, 3];
    private static readonly int[] TwoEntryIds = [1, 2];
    private static readonly string[] FirstSuggestions = ["first", "second"];
    private static readonly string[] SecondSuggestions = ["second", "third"];

    [Test]
    public void InsertEntries_Should_Insert_A_Batch_At_Requested_Position_And_Renumber()
    {
        var service = new GraphEditingService(BuiltinSchemas.CreateCatalog());
        var node = new GraphNode(new Group { Name = "Group" }, GraphNodeKind.LinearGroup);
        var inserted = service.InsertEntries(node, 0, 3);

        Assert.That(inserted, Has.Count.EqualTo(3));
        Assert.That(node.Entries, Is.EqualTo(inserted));
        Assert.That(node.Entries.Select(entry => entry.Id), Is.EqualTo(ThreeEntryIds));
        Assert.That(inserted.All(entry => entry.Type == TextEntry.TypeId), Is.True);
    }

    [Test]
    public void InsertEntries_Should_Clamp_Index_And_Reject_Invalid_Count()
    {
        var service = new GraphEditingService(BuiltinSchemas.CreateCatalog());
        var node = new GraphNode(new Group { Name = "Group" }, GraphNodeKind.LinearGroup);

        var appended = service.InsertEntries(node, int.MaxValue, 2);
        var rejected = service.InsertEntries(node, 0, 0);

        Assert.That(node.Entries.TakeLast(2), Is.EqualTo(appended));
        Assert.That(rejected, Is.Empty);
        Assert.That(node.Entries.Select(entry => entry.Id), Is.EqualTo(TwoEntryIds));
    }

    [Test]
    public void Variable_Target_Field_Should_Expose_Definitions_And_Update_The_Entry()
    {
        var entry = new EntryEditorItemViewModel
        {
            Type = SetVariableEntry.TypeId,
            Parameters = new Dictionary<string, string>
            {
                ["target"] = "first",
                ["expression"] = "true"
            }
        };

        var catalog = BuiltinSchemas.CreateCatalog();
        entry.ConfigureParameterFields(catalog, [], ["first", "second"]);
        var target = entry.ParameterFields.OfType<VariableNameEntryParameterEditorItemViewModel>().Single();

        Assert.That(target.Suggestions, Is.EqualTo(FirstSuggestions));
        Assert.That(target.StringValue, Is.EqualTo("first"));

        target.StringValue = "second";
        Assert.That(entry.Parameters["target"], Is.EqualTo("second"));

        entry.ConfigureParameterFields(catalog, [], ["second", "third"]);
        Assert.That(entry.ParameterFields.Single(field => field.Id == "target").Suggestions,
            Is.EqualTo(SecondSuggestions));
    }
}
