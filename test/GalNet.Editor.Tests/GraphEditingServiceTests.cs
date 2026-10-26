using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Controls;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Primitives.Builtins;

namespace GeneralTest.Editor;

public class GraphEditingServiceTests
{
    private static readonly string[] FirstSuggestions = ["first", "second"];
    private static readonly string[] SecondSuggestions = ["second", "third"];
    [Test]
    public void GraphEdge_Should_Refresh_When_Target_Connector_Layout_Changes()
    {
        var source = new GraphNode(new Group { Id = "source", Name = "Source" }, GraphNodeKind.LinearGroup) { Y = 100 };
        var target = new GraphNode(new EditorGraphNodeDto
        {
            Id = "branch",
            Type = "Branch",
            BranchType = "Choice",
            Name = "Branch",
            Y = 100,
            Options = [new EditorGraphBranchOptionDto { Id = "option-1", Text = "Option 1" }]
        }, GraphNodeKind.ChoiceBranch);
        using var edge = new GraphEdge(source, target);
        var initialEndY = edge.EndY;
        var pathChanges = 0;
        edge.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GraphEdge.PathData))
                pathChanges++;
        };

        target.Options.Add(new BranchOptionEditorItemViewModel { Id = "option-2", Text = "Option 2" });
        target.Options.Add(new BranchOptionEditorItemViewModel { Id = "option-3", Text = "Option 3" });
        target.Options.Add(new BranchOptionEditorItemViewModel { Id = "option-4", Text = "Option 4" });
        target.RefreshConnectors();

        Assert.Multiple(() =>
        {
            Assert.That(target.LayoutHeight, Is.GreaterThan(GraphLayoutMetrics.NodeMinHeight));
            Assert.That(edge.EndY, Is.GreaterThan(initialEndY));
            Assert.That(pathChanges, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Variable_Target_Field_Should_Expose_Definitions_And_Update_The_Entry()
    {
        var entry = new EntryEditorItemViewModel
        {
            Type = BuiltinEntryIds.SetVariable,
            Parameters = new Dictionary<string, string>
            {
                ["target"] = "first",
                ["expression"] = "true"
            }
        };

        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
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

    [Test]
    public async Task MixedDtoFieldsRetainAssetPickerAndSharedBindingWithoutDuplicatingSchema()
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var entry = new EntryEditorItemViewModel { Type = BuiltinEntryIds.ReplaceLayer };
        entry.ConfigureParameterFields(catalog, [], []);
        Assert.That(entry.ParameterForm, Is.Null);
        var asset = entry.ParameterFields.OfType<AssetEntryParameterEditorItemViewModel>().Single();
        var handle = entry.ParameterFields.OfType<SchemaEntryParameterEditorItemViewModel>().Single();
        Assert.That(asset.AssetFilter, Is.EqualTo(GalNet.Editor.Models.AssetPickerFilter.Image));
        Assert.That(handle.Definition.Field, Is.SameAs(catalog.Get(entry.Type).DynamicParameters["handleId"].Field));
        asset.StringValue = "background";
        Assert.That(await handle.CommitAsync(new(JsonSerializer.SerializeToElement("scene")), default), Is.Null);
        Assert.That(entry.Parameters, Is.EquivalentTo(new Dictionary<string, string> { ["handleId"] = "scene", ["assetId"] = "background" }));
        entry.Type = BuiltinEntryIds.HideLayer;
        entry.ConfigureParameterFields(catalog, [], []);
        Assert.That(entry.ParameterFields, Is.Empty);
        Assert.That(entry.ParameterForm!.Fields.Single().Id, Is.EqualTo("handleId"));
        Assert.That(await handle.CommitAsync(new(JsonSerializer.SerializeToElement("stale")), default), Is.Not.Null);
    }
}
