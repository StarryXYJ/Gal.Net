using System.Text.Json;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor.Shared;

public sealed class ToolOperationCatalogTests
{
    private static readonly string[] RequiredNullableInput = ["commandId", "name", "optional"];
    [Test]
    public void Metadata_DescribesActualJsonTypesDefaultsAndEnums()
    {
        var catalog = new ToolOperationCatalog();
        var patch = catalog.Find("group.entry.patchParameters")!.InputSchema;
        Assert.That(patch.GetProperty("properties").GetProperty("parameters").GetProperty("type").GetString(), Is.EqualTo("object"));
        var kind = catalog.Find("graph.node.create")!.InputSchema.GetProperty("properties").GetProperty("kind");
        Assert.That(kind.GetProperty("enum").EnumerateArray().Select(item => item.GetString()), Does.Contain("LinearGroup"));
        var delete = catalog.Find("graph.node.delete")!.InputSchema;
        Assert.That(delete.GetProperty("properties").GetProperty("deleteGroupEntries").GetProperty("default").GetBoolean(), Is.True);
        var positions = catalog.Find("graph.node.move")!.InputSchema.GetProperty("properties").GetProperty("nodes");
        Assert.That(positions.GetProperty("items").GetProperty("properties").TryGetProperty("nodeId", out _), Is.True);
    }

    [Test]
    public void NullableInputWithoutDefault_IsRequiredButAcceptsNull_AndCommandIdIsConst()
    {
        var schema = CommandSchemaBuilder.Build(typeof(NullableInputCommand), "test.nullable").JsonSchema!.Value;
        Assert.That(schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()), Is.EqualTo(RequiredNullableInput));
        Assert.That(schema.GetProperty("properties").GetProperty("commandId").GetProperty("const").GetString(), Is.EqualTo("test.nullable"));
        Assert.That(schema.GetProperty("properties").GetProperty("optional").GetProperty("type").EnumerateArray().Select(item => item.GetString()), Does.Contain("null"));
    }

    [Test]
    public void HelpAndRouting_UseTheSameDefinitions_WithoutLegacyAliases()
    {
        var catalog = new ToolOperationCatalog();
        var all = ToolHelp.Select(catalog, CliInvocation.Parse(catalog, ["help", "--json"]));
        Assert.That(all.Select(definition => definition.OperationId).Distinct().Count(), Is.EqualTo(all.Count));
        foreach (var definition in all.Where(item => item.Transport == "cli"))
            Assert.That(catalog.Resolve(definition.CommandPath.Split(' ')).Definition, Is.SameAs(definition));
        var target = ToolHelp.Select(catalog, CliInvocation.Parse(catalog, ["help", "graph", "nodes"]));
        Assert.That(target.Single().OperationId, Is.EqualTo("graph.nodes"));
        Assert.That(catalog.Search("entry").All(definition => definition.InputSchema.ValueKind == JsonValueKind.Object), Is.True);
        Assert.That(catalog.Search("not-a-real-operation"), Is.Empty);
    }

    [TestCase("help --unexpected")]
    [TestCase("help --search")]
    [TestCase("help --json --json")]
    [TestCase("help graph node --search graph")]
    [TestCase("graph node")]
    [TestCase("build project --output")]
    [TestCase("graph nodes project unexpected")]
    [TestCase("graph nodes project --fake")]
    [TestCase("commands")]
    [TestCase("summary project")]
    [TestCase("create project")]
    [TestCase("nodes project")]
    [TestCase("group show project")]
    [TestCase("group entries project --id a --file Graph/a.rawgalgroup")]
    public void UnknownOrConflictingArguments_FailBeforeProjectLoading(string command)
    {
        Assert.Throws<ArgumentException>(() => CliInvocation.Parse(new ToolOperationCatalog(), command.Split(' ')));
    }

    [Test]
    public void InstalledGameInspection_IsReadOnlyAndRequiresNoAuthoringContract()
    {
        var catalog = new ToolOperationCatalog();
        var invocation = CliInvocation.Parse(catalog, ["game", "inspect", "app/pak"]);
        var definition = catalog.Find("game.inspect")!;
        Assert.That(definition.ReadOnly, Is.True);
        Assert.That(definition.RequiresProject, Is.False);
        Assert.That(definition.RequiresContract, Is.False);
        Assert.That(invocation.Positionals, Is.EqualTo(new[] { "app/pak" }));
    }

    [Test]
    public void FileDryRun_IsNotAdvertised_AndUnknownHelpTargetFails()
    {
        var catalog = new ToolOperationCatalog();
        Assert.That(catalog.Find("asset.import")!.SupportsDryRun, Is.False);
        Assert.That(catalog.Find("group.entry.add")!.SupportsDryRun, Is.True);
        Assert.Throws<ArgumentException>(() => ToolHelp.Select(catalog, CliInvocation.Parse(catalog, ["help", "missing"])));
    }

    [Test]
    public void RequiredFlags_AndDocumentInputs_AreVisibleInTextHelp()
    {
        var rename = new ToolOperationCatalog().Find("project.rename")!;
        Assert.That(rename.Usage, Does.Contain("--name <value>").And.Not.Contain("[--name"));
        Assert.That(ToolHelp.RenderText([rename], true), Does.Contain("Document input (execute):").And.Contain("commandId"));
    }

    [Test]
    public void DefaultCommandDeserializer_IsStrictForEveryConsumer()
    {
        using var document = JsonDocument.Parse("""{"commandId":"project.rename","name":"New","future":true}""");
        Assert.Throws<JsonException>(() => new EditorCommandCatalog().Deserialize("project.rename", document.RootElement));
        using var file = JsonDocument.Parse("""{"commandId":"asset.directory.create","name":"New","parentDirectory":"","future":true}""");
        Assert.Throws<JsonException>(() => new AssetFileCommandCatalog().Deserialize("asset.directory.create", file.RootElement));
    }

    public sealed record NullableInputCommand(string Name, string? Optional) : IProjectEditCommand
    {
        public string CommandId => "test.nullable";
    }
}
