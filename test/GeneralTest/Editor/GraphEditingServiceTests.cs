using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;

namespace GeneralTest.Editor;

public class GraphEditingServiceTests
{
    [Test]
    public void InsertEntries_Should_Insert_A_Batch_At_Requested_Position_And_Renumber()
    {
        var service = new GraphEditingService();
        var node = new GraphNode(new Group { Name = "Group" }, GraphNodeKind.LinearGroup);
        var original = node.Entries.Single();

        var inserted = service.InsertEntries(node, 0, 3);

        Assert.That(inserted, Has.Count.EqualTo(3));
        Assert.That(node.Entries, Has.Count.EqualTo(4));
        Assert.That(node.Entries.Take(3), Is.EqualTo(inserted));
        Assert.That(node.Entries[3], Is.SameAs(original));
        Assert.That(node.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(inserted.All(entry => entry.Type == TextEntry.TypeId), Is.True);
    }

    [Test]
    public void InsertEntries_Should_Clamp_Index_And_Reject_Invalid_Count()
    {
        var service = new GraphEditingService();
        var node = new GraphNode(new Group { Name = "Group" }, GraphNodeKind.LinearGroup);

        var appended = service.InsertEntries(node, int.MaxValue, 2);
        var rejected = service.InsertEntries(node, 0, 0);

        Assert.That(node.Entries.TakeLast(2), Is.EqualTo(appended));
        Assert.That(rejected, Is.Empty);
        Assert.That(node.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { 1, 2, 3 }));
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

        entry.ConfigureParameterFields([], ["first", "second"]);
        var target = entry.ParameterFields.OfType<VariableNameEntryParameterEditorItemViewModel>().Single();

        Assert.That(target.Suggestions, Is.EqualTo(new[] { "first", "second" }));
        Assert.That(target.StringValue, Is.EqualTo("first"));

        target.StringValue = "second";
        Assert.That(entry.Parameters["target"], Is.EqualTo("second"));

        entry.ConfigureParameterFields([], ["second", "third"]);
        Assert.That(entry.ParameterFields.Single(field => field.Id == "target").Suggestions,
            Is.EqualTo(new[] { "second", "third" }));
    }
}
