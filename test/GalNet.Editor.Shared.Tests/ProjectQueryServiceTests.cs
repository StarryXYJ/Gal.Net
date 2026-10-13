using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ProjectQueryServiceTests
{
    private string _root = null!;
    private const string Graph = """
        {"version":2,"name":"Original","rootNodeId":"a","nodes":[
          {"id":"a","type":"Group","name":"A","file":"groups/a.rawgalgroup","future":{"kept":true}},
          {"id":"b","type":"Group","name":"B","file":"groups/b.rawgalgroup"}],
         "edges":[{"id":"ab","fromNodeId":"a","fromOutlet":0,"toNodeId":"b"}],
         "playerVariables":[{"name":"score","future":123}],"saveVariables":[]}
        """;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-query-" + Guid.NewGuid());
        Put("Graph/graph.json", Graph);
        Put("settings.json", "{\"title\":\"Original\"}");
        Put("Graph/groups/a.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"same","type":"unknown.custom","parameters":{"future":{"kept":true},"condition":"score > 2"}},{"type":"unknown.other","parameters":{}}]}""");
        Put("Graph/groups/b.rawgalgroup", """{"version":2,"kind":"Raw","entries":[{"id":"same","type":"unknown.other","parameters":{}}]}""");
        Put("Graph/groups/orphan.rawgalgroup", """{"version":2,"kind":"Raw","entries":[]} """);
        Put("Assets/picture.bin", "asset");
        Put("Assets/picture.bin.meta", new AssetMetaCodec(BuiltinResourceTypes.CreateCatalog()).Serialize(new BinaryAssetMeta
        { Id = "f2700971-d36e-4be4-94dd-a5c282861c57", TypeId = "data", Path = "picture.bin" }));
        Put("Output/ignored.json", "{}");
        Put("Contracts/placeholder.txt", "not a schema");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void RawQueries_PreserveUnknownContentAndMissingIds_WithoutSchemaOrWrites()
    {
        var before = Snapshot();
        var service = new ProjectQueryService(_root);
        var entries = (QueryPage<ProjectObjectItem>)service.Group(new(Id: "a"), entries: true).Data!;
        Assert.Multiple(() =>
        {
            Assert.That(entries.Total, Is.EqualTo(2));
            Assert.That(entries.Items[0].Content.GetProperty("parameters").GetProperty("future").GetProperty("kept").GetBoolean(), Is.True);
            Assert.That(entries.Items[1].Id, Is.Null);
            Assert.That(service.Graph("graph.show").Success, Is.True);
            Assert.That(service.Variables("player", "score").Success, Is.True);
            Assert.That(service.Info().Success, Is.True);
        });
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public void Find_ReturnsAllDuplicateEntryIds_AndGuidMatches()
    {
        var service = new ProjectQueryService(_root);
        var all = (QueryPage<ProjectObjectItem>)service.Find(new("same", "entry")).Data!;
        var limited = (QueryPage<ProjectObjectItem>)service.Find(new("same", "entry", "a")).Data!;
        var resource = (QueryPage<ProjectObjectItem>)service.Find(new("F2700971D36E4BE494DDA5C282861C57", "resource")).Data!;
        Assert.Multiple(() =>
        {
            Assert.That(all.Total, Is.EqualTo(2));
            Assert.That(limited.Total, Is.EqualTo(1));
            Assert.That(resource.Items.Single().File, Is.EqualTo("Assets/picture.bin.meta"));
            Assert.That(resource.Items.Single().JsonPointer, Is.EqualTo("/id"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResourceQueries_ReadActualCodecOutput_AndDoNotCountTheIdentityAsAReference(bool usePascalCase)
    {
        var resourceId = usePascalCase ? "f2700971-d36e-4be4-94dd-a5c282861c57" : "f2700971d36e4be494dda5c282861c57";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = usePascalCase ? null : JsonNamingPolicy.CamelCase };
        Put("Assets/picture.bin.meta", new AssetMetaCodec(BuiltinResourceTypes.CreateCatalog(), options).Serialize(new BinaryAssetMeta
        { Id = resourceId, TypeId = "data", Path = "picture.bin" }));
        var query = new ProjectQueryService(_root);
        var files = query.Files(new(Kind: "resource"));
        Assert.That(files.Success, Is.True);
        Assert.That(((QueryPage<ProjectFileItem>)files.Data!).Items.Single().ResourceId, Is.EqualTo(resourceId));
        var found = (QueryPage<ProjectObjectItem>)query.Find(new("f2700971d36e4be494dda5c282861c57", "resource")).Data!;
        Assert.That(found.Items.Single().JsonPointer, Is.EqualTo(usePascalCase ? "/Id" : "/id"));
        Assert.That(found.Items.Single().Id, Is.EqualTo(resourceId), "Preserve the provider's identifier spelling for use in authored entries.");
        Assert.That(((QueryPage<ProjectReferenceItem>)query.References("f2700971-d36e-4be4-94dd-a5c282861c57", "resource").Data!).Items, Is.Empty);
    }

    [Test]
    public void AmbiguousMetadataIdentities_AreDiagnosedInsteadOfSilentlySelectingOne()
    {
        Put("Assets/picture.bin.meta", """{"id":"f2700971-d36e-4be4-94dd-a5c282861c57","Id":"f2700971-d36e-4be4-94dd-a5c282861c57"}""");
        var result = new ProjectQueryService(_root).Find(new("f2700971-d36e-4be4-94dd-a5c282861c57", "resource"));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Single().Code, Is.EqualTo("asset.guidInvalid"));
    }

    [Test]
    public void Files_UseIntersectionFilters_SortedExplicitPagination_AndRootOverride()
    {
        var service = new ProjectQueryService(_root);
        var page = (QueryPage<ProjectFileItem>)service.Files(new(Glob: "**/*.rawgalgroup", Extension: ".rawgalgroup", Kind: "rawgroup", Offset: 1, Limit: 1)).Data!;
        var all = (QueryPage<ProjectFileItem>)service.Files(new()).Data!;
        var contracts = (QueryPage<ProjectFileItem>)service.Files(new(Root: "Contracts")).Data!;
        Assert.Multiple(() =>
        {
            Assert.That(page.Total, Is.EqualTo(3));
            Assert.That(page.Items.Single().File, Is.EqualTo("Graph/groups/b.rawgalgroup"));
            Assert.That(page.HasMore, Is.True);
            Assert.That(all.Items.Select(item => item.File), Is.Ordered.Using<string>(StringComparer.Ordinal));
            Assert.That(all.Items.Any(item => item.File.StartsWith("Output/", StringComparison.Ordinal) || item.File.StartsWith("Contracts/", StringComparison.Ordinal)), Is.False);
            Assert.That(contracts.Total, Is.EqualTo(1));
        });
    }

    [Test]
    public void InvalidMetadata_IsDiagnosed_WhileFileRemainsListed()
    {
        Put("Assets/picture.bin.meta", "{broken");
        var result = new ProjectQueryService(_root).Files(new(Kind: "resource"));
        var page = (QueryPage<ProjectFileItem>)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(page.Total, Is.EqualTo(1));
            Assert.That(page.Items.Single().File, Is.EqualTo("Assets/picture.bin"));
            Assert.That(page.Complete, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Code == "json.invalid" && item.Line is not null), Is.True);
        });
    }

    [Test]
    public void Groups_IncludeOrphans_AndRejectAmbiguousSelectorsAndPathEscape()
    {
        var service = new ProjectQueryService(_root);
        var groups = (QueryPage<ProjectGroupItem>)service.Groups().Data!;
        Assert.That(groups.Items.Single(item => !item.Referenced).File, Is.EqualTo("Graph/groups/orphan.rawgalgroup"));
        Assert.Throws<ArgumentException>(() => service.Group(new(), entries: true));
        Assert.Throws<ArgumentException>(() => service.Group(new("a", "Graph/groups/a.rawgalgroup"), entries: true));
        Assert.Throws<ArgumentException>(() => service.Files(new(Root: "../")));
        Assert.Throws<ArgumentException>(() => service.Group(new(File: "../outside.rawgalgroup"), entries: false));
        Assert.Throws<ArgumentException>(() => service.Files(new(Offset: -1)));
    }

    [Test]
    public void References_ReportLocations_AndIncompleteAnalysis()
    {
        var result = new ProjectQueryService(_root).References("score", "variable");
        var references = (QueryPage<ProjectReferenceItem>)result.Data!;
        Assert.Multiple(() =>
        {
            Assert.That(references.Items.Single().JsonPointer, Is.EqualTo("/entries/0/parameters/condition"));
            Assert.That(references.Complete, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Code == "references.partial"), Is.True);
        });
    }

    [Test]
    public async Task CliQueries_WorkWithoutAContract_AndPreserveOriginalBytes()
    {
        var before = Snapshot();
        var (exit, output, error) = await GeneralTest.Editor.Shared.CliHelpTests.Invoke(["find", _root, "same", "--kind", "entry", "--limit", "1"]);
        using var result = JsonDocument.Parse(output);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, error);
            Assert.That(result.RootElement.GetProperty("data").GetProperty("total").GetInt32(), Is.EqualTo(2));
            Assert.That(result.RootElement.GetProperty("data").GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(Snapshot(), Is.EqualTo(before));
        });
        var (schemaExit, schemaOutput, _) = await GeneralTest.Editor.Shared.CliHelpTests.Invoke(["schema", "list", _root]);
        using var schemaResult = JsonDocument.Parse(schemaOutput);
        Assert.That(schemaExit, Is.EqualTo(1));
        Assert.That(schemaResult.RootElement.GetProperty("diagnostics")[0].GetProperty("message").GetString(), Does.Contain("contract is missing"));
    }

    [Test]
    public void DirectoryLinks_AreSkipped_AndExplicitTraversalIsRejected()
    {
        var outside = Path.Combine(Path.GetTempPath(), "GalNet-query-outside-" + Guid.NewGuid());
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "linked");
        try
        {
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            { Assert.Ignore($"Creating a directory link is unavailable: {exception.Message}"); }
            var result = new ProjectQueryService(_root).Files(new());
            Assert.That(result.Diagnostics.Any(item => item.Code == "file.linkSkipped"), Is.True);
            Assert.Throws<InvalidDataException>(() => new ProjectQueryService(_root).Files(new(Root: "linked")));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(outside);
        }
    }

    private void Put(string relative, string content)
    {
        var file = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private string[] Snapshot() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(file => Path.GetRelativePath(_root, file) + ":" + Convert.ToHexString(File.ReadAllBytes(file))).ToArray();
}
