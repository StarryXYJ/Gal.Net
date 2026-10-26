using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class AuthoringDraftPersistenceTests
{
    private string _root = null!;
    private IEntryCatalog _catalog = null!;
    private const string Source = "Graph/groups/a.rawgalgroup";
    private const string Graph = """{"version":2,"name":"Draft","rootNodeId":"a","nodes":[{"id":"a","name":"A","type":"Group","file":"groups/a.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""";
    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-drafts-" + Guid.NewGuid())).FullName;
        _catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        Put("Graph/graph.json", Graph); Put("settings.json", "{}");
        Put(Source, """{"version":2,"kind":"Raw","entries":[]}""");
    }
    [TearDown] public void TearDown() => Directory.Delete(_root, true);

    [TestCase("type"), TestCase("parameter"), TestCase("incomplete"), TestCase("value"), TestCase("condition")]
    public void AuthoringRoundTripsDraftJsonWhileCompleteValidationStillRejectsIt(string kind)
    {
        var entry = new GroupEntryDocument { Id = "stable-line", Type = BuiltinEntryIds.Wait, Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1) } };
        switch (kind)
        {
            case "type": entry.Type = "custom.future"; entry.Parameters = new() { ["opaque"] = JsonSerializer.Deserialize<JsonElement>("{\"large\":9007199254740993,\"items\":[null,{\"kept\":true}]}") }; break;
            case "parameter": entry.Parameters["future"] = JsonSerializer.SerializeToElement<object?>(null); break;
            case "incomplete": entry.Type = BuiltinEntryIds.PlayParticleEmitter; entry.Parameters.Clear(); break;
            case "value": entry.Parameters["duration"] = JsonSerializer.SerializeToElement(new { invalidButPreserved = true }); break;
            case "condition": entry.Condition = "("; break;
        }
        Put(Source, JsonSerializer.Serialize(new GroupDocument { Entries = [entry] }));
        var original = JsonSerializer.SerializeToElement(entry.Parameters);
        var repository = new EditorDocumentRepository(_catalog, EditorDocumentValidation.Authoring);
        var loaded = repository.Load(_root);
        loaded.Document.Name = "Saved draft";
        repository.Save(_root, loaded.Document, loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        var reopened = new EditorDocumentRepository(_catalog, EditorDocumentValidation.Authoring).Load(_root);
        var saved = reopened.GroupEntries["a"].Single();
        Assert.That(saved.StableId, Is.EqualTo(entry.Id)); Assert.That(saved.Type, Is.EqualTo(entry.Type)); Assert.That(saved.Condition, Is.EqualTo(entry.Condition));
        Assert.That(JsonElement.DeepEquals(original, JsonSerializer.SerializeToElement(saved.Parameters)), Is.True, "Saving must not normalize, insert defaults, or coerce invalid values.");
        Assert.That(() => new EditorDocumentRepository(_catalog).Load(_root), Throws.TypeOf<InvalidDataException>());
        var result = new ProjectValidationService(_root).Validate(catalog: _catalog);
        Assert.That(result.Success, Is.False); Assert.That(result.Diagnostics.Any(item => item.Code.StartsWith("entry.", StringComparison.Ordinal) || item.Code == "condition.syntaxInvalid"), Is.True);
        if (kind != "condition") Assert.That(() => GalgroupCompiler.Compile(new GroupDocument { Entries = [entry] }, _catalog), Throws.Exception);
    }

    [TestCase(""), TestCase("(")]
    public void AuthoringGraphConditionsRemainDraftsWithoutSkippingGraphTopologyOrSourceChecks(string expression)
    {
        var graph = JsonSerializer.Deserialize<EditorGraphDocument>(Graph)!;
        graph.Nodes.Add(new() { Id = "branch", Name = "Branch", Type = "Branch", BranchType = "Condition", Conditions = [new() { Id = "condition", Expression = expression }] });
        Put("Graph/graph.json", JsonSerializer.Serialize(graph));
        var repository = new EditorDocumentRepository(_catalog, EditorDocumentValidation.Authoring);
        var loaded = repository.Load(_root);
        repository.Save(_root, loaded.Document, loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value));
        Assert.That(repository.Load(_root).Document.Nodes[1].Conditions![0].Expression, Is.EqualTo(expression));
        Assert.That(new ProjectValidationService(_root).Validate("graph").Success, Is.False);
        loaded.Document.Edges.Add(new() { Id = "broken", FromNodeId = "missing", ToNodeId = "a" });
        Assert.That(() => repository.PrepareSave(_root, loaded.Document, loaded.GroupEntries.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<EditorEntryData>)pair.Value)), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("{ broken")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"custom\":true,\"entries\":[]}")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"type\":\"future\",\"parameters\":{}}]}")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"id\":\"x\",\"type\":\"future\",\"parameters\":{}}]}")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"type\":\"future\",\"extra\":true,\"parameters\":{}}]}")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"type\":\"future\",\"parameters\":[]}]}")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"type\":\"future\",\"parameters\":{}},{\"id\":\"e\",\"type\":\"future\",\"parameters\":{}}]}")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"type\":\"future\",\"parameters\":{\"opaque\":{\"x\":1,\"x\":2}}}]}")]
    public void AuthoringStillRejectsMalformedOrLossySourceBeforeWriting(string raw)
    {
        Put(Source, raw); var bytes = File.ReadAllBytes(Path.Combine(_root, Source));
        var repository = new EditorDocumentRepository(_catalog, EditorDocumentValidation.Authoring);
        Assert.That(() => repository.Load(_root), Throws.TypeOf<InvalidDataException>());
        var graph = JsonSerializer.Deserialize<EditorGraphDocument>(Graph)!;
        Assert.That(() => new EditorDocumentRepository(_catalog, EditorDocumentValidation.Authoring).Save(_root, graph, new Dictionary<string, IReadOnlyList<EditorEntryData>> { ["a"] = [] }), Throws.TypeOf<InvalidDataException>());
        Assert.That(File.ReadAllBytes(Path.Combine(_root, Source)), Is.EqualTo(bytes));
    }

    [TestCase("../outside.rawgalgroup"), TestCase("/outside.rawgalgroup"), TestCase("groups/missing.rawgalgroup")]
    public void AuthoringDoesNotSkipSourcePathsAndExistence(string path)
    {
        var graph = JsonSerializer.Deserialize<EditorGraphDocument>(Graph)!; graph.Nodes[0].File = path;
        Put("Graph/graph.json", JsonSerializer.Serialize(graph));
        Assert.That(() => new EditorDocumentRepository(_catalog, EditorDocumentValidation.Authoring).Load(_root), Throws.TypeOf<InvalidDataException>());
    }

    private void Put(string relative, string text)
    {
        var path = Path.Combine(_root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
    }

    [Test]
    public void ExplicitStructureOnlyRemainsStructuralEvenWithAnInjectedCatalog()
    {
        var raw = JsonSerializer.Deserialize<JsonElement>("""{"version":2,"kind":"Raw","entries":[{"id":"future","type":"future.custom","condition":"(","parameters":{"unknown":null}}]}""");
        var diagnostics = new List<ToolDiagnostic>();
        Assert.That(ProjectValidationService.ValidateGroupDocument(raw, Source, diagnostics, _catalog, structureOnly: true), Is.Not.Null);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(ProjectValidationService.ValidateGroupDocument(raw, Source, diagnostics, _catalog), Is.Null);
        Assert.That(diagnostics.Any(item => item.Code == "entry.typeUnknown"), Is.True);
    }
}
