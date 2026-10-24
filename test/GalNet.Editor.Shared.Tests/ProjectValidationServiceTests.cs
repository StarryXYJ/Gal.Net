using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Services;
using GeneralTest.Editor.Shared;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectValidationServiceTests
{
    private string _root = null!;
    private const string Source = "Graph/groups/a.rawgalgroup";
    private const string Graph = """
        {"version":2,"name":"Test","rootNodeId":"a","nodes":[{"id":"a","name":"A","type":"Group","file":"groups/a.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}
        """;
    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-validation-" + Guid.NewGuid())).FullName;
        Put("Graph/graph.json", Graph);
        Put("settings.json", "{}");
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"id":"e","type":"custom.test","parameters":{"value":2,"mode":"one"}}]}""");
    }
    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void LocalizationScopeIsCheckedAndOpaqueContractsAreExplicitlyIncomplete()
    {
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"id":"opaque","type":"opaque","parameters":{"blob":{"future":"unmarked"}}}]}""");
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("opaque", new DynamicParameterTable([new("blob", typeof(JsonElement))]))])]);
        var result = new ProjectValidationService(_root).Validate(catalog: catalog);
        Assert.That(result.Success, Is.True, string.Join(";", result.Diagnostics.Select(item => item.Message)));
        var report = (ProjectValidationReport)result.Data!;
        Assert.That(report.Checks, Does.Contain("localization-semantics")); Assert.That(report.Complete, Is.False);
        Assert.That(result.Diagnostics.Any(item => item.Code == "localization.incomplete" && item.Severity == "warning"), Is.True);
    }

    [Test]
    public void StructureOnly_NeedsNoDll_AndReportsSkippedSemanticChecks()
    {
        var before = Snapshot();
        var service = new ProjectValidationService(_root);
        var structure = service.Validate(structureOnly: true);
        var semantic = service.Validate();
        Assert.Multiple(() =>
        {
            Assert.That(structure.Success, Is.True);
            Assert.That(((ProjectValidationReport)structure.Data!).SemanticStatus, Is.EqualTo("skipped"));
            Assert.That(((ProjectValidationReport)structure.Data!).SkippedChecks, Does.Contain("entry-contracts"));
            Assert.That(semantic.Diagnostics.Select(item => item.Code), Does.Contain("contract.load"));
            Assert.That(((ProjectValidationReport)semantic.Data!).Complete, Is.False);
            Assert.That(Snapshot(), Is.EqualTo(before));
        });
    }

    [TestCase("\"Author notes\"", true)]
    [TestCase("42", false)]
    [TestCase("null", false)]
    public void VariableDescriptionIsOptionalTextAndDoesNotRelaxUnknownFields(string description, bool valid)
    {
        var variable = "{\"name\":\"score\",\"description\":" + description + ",\"type\":1,\"defaultValue\":{\"Uid\":\"score-id\",\"Name\":\"score\",\"Type\":1,\"Value\":0}}";
        Put("Graph/graph.json", Graph.Replace("\"saveVariables\":[]", "\"saveVariables\":[" + variable + "]", StringComparison.Ordinal));
        var report = new ProjectValidationService(_root).Validate("graph", structureOnly: true);
        Assert.That(report.Success, Is.EqualTo(valid));
        if (!valid) Assert.That(report.Diagnostics.Any(item => item.JsonPointer == "/saveVariables/0/description"), Is.True);
    }

    [TestCase("{}", "document.versionInvalid")]
    [TestCase("{\"version\":1,\"kind\":\"Raw\",\"entries\":[]}", "document.versionInvalid")]
    [TestCase("{\"version\":2,\"kind\":\"Compiled\",\"entries\":[]}", "group.kindInvalid")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"future\":true,\"entries\":[]}", "json.fieldUnknown")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"type\":\"x\",\"parameters\":{}}]}", "json.stringRequired")]
    [TestCase("{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"id\":\"e\",\"type\":\"x\",\"parameters\":{}}]}", "json.propertyDuplicate")]
    [TestCase("{ broken", "json.invalid")]
    public void RawStructuralErrors_HaveLocations_AndNeverRepair(string json, string code)
    {
        Put(Source, json);
        var result = new ProjectValidationService(_root).Validate("group", new(File: Source), structureOnly: true);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Any(item => item.Code == code && item.File == Source), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_root, Source)), Is.EqualTo(json));
    }

    [TestCase("{\"value\":2,\"mode\":\"one\",\"unknown\":1}", "Unknown primitive parameter")]
    [TestCase("{\"mode\":\"one\"}", "requires parameter")]
    [TestCase("{\"value\":\"2\",\"mode\":\"one\"}", "compatible")]
    [TestCase("{\"value\":2,\"mode\":\"invalid\"}", "unsupported value")]
    [TestCase("{\"value\":-1,\"mode\":\"one\"}", "positive")]
    public void SemanticParameters_UseCurrentContractIncludingCustomValidator(string parameters, string message)
    {
        Put(Source, "{\"version\":2,\"kind\":\"Raw\",\"entries\":[{\"id\":\"e\",\"type\":\"custom.test\",\"parameters\":" + parameters + "}]}");
        var result = new ProjectValidationService(_root).Validate("group", new(File: Source), catalog: Catalog());
        Assert.That(result.Diagnostics.Any(item => item.Code == "entry.parametersInvalid" && item.ObjectId == "e" && item.JsonPointer == "/entries/0/parameters" && item.Message.Contains(message, StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void SelectedGroup_IgnoresUnrelatedBrokenGroupAndGraph_AndDoesNotEvaluateConditions()
    {
        Put("Graph/graph.json", "broken");
        Put("Graph/groups/unrelated.rawgalgroup", "broken");
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"id":"e","type":"custom.test","condition":"1 / 0 > 2","parameters":{"value":2,"mode":"one"}}]}""");
        var before = Snapshot();
        var result = new ProjectValidationService(_root).Validate("group", new(File: Source), catalog: Catalog());
        Assert.That(result.Success, Is.True, string.Join(";", result.Diagnostics));
        Assert.That(((ProjectValidationReport)result.Data!).CheckedFiles, Is.EqualTo(new[] { Source }));
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public void GroupIdSelector_DoesNotInspectUnrelatedGroupPaths()
    {
        Put("Graph/graph.json", Graph.Replace("\"nodes\":[", "\"nodes\":[{\"id\":\"other\",\"type\":\"Group\",\"name\":\"Other\",\"file\":\"../../outside.rawgalgroup\"},", StringComparison.Ordinal));
        var result = new ProjectValidationService(_root).Validate("group", new(Id: "a"), catalog: Catalog());
        Assert.That(result.Success, Is.True, string.Join(";", result.Diagnostics));
    }

    [Test]
    public void UnknownEntry_AndConditionSyntaxErrors_AreExplicit()
    {
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"id":"e","type":"unknown","condition":"1 > (","parameters":{}}]}""");
        var result = new ProjectValidationService(_root).Validate("group", new(File: Source), catalog: Catalog());
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain("entry.typeUnknown").And.Contain("condition.syntaxInvalid"));
    }

    [Test]
    public void GraphTopology_AndSources_AreChecked_WithoutEntryDll()
    {
        Put("Graph/graph.json", Graph.Replace("\"edges\":[]", "\"edges\":[{\"id\":\"edge\",\"fromNodeId\":\"a\",\"fromOutlet\":9,\"toNodeId\":\"missing\"}]", StringComparison.Ordinal));
        File.Delete(Path.Combine(_root, Source));
        var result = new ProjectValidationService(_root).Validate("graph");
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain("graph.edge.invalidOutlet").And.Contain("graph.edge.toNotFound").And.Contain("group.sourceMissing"));
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Not.Contain("contract.load"));
    }

    [Test]
    public void FullProject_IncludesOrphansAndMetadata_ButIdsAreScopedToGroup()
    {
        Put("Graph/groups/orphan.rawgalgroup", File.ReadAllText(Path.Combine(_root, Source)));
        Put("Assets/missing.bin.meta", "{\"id\":\"not-a-guid\"}");
        var result = new ProjectValidationService(_root).Validate(catalog: Catalog());
        Assert.That(((ProjectValidationReport)result.Data!).CheckedFiles, Does.Contain("Graph/groups/orphan.rawgalgroup"));
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Contain("asset.guidInvalid").And.Contain("asset.fileMissing"));
        Assert.That(result.Diagnostics.Select(item => item.Code), Does.Not.Contain("identity.duplicate"));
    }

    [Test]
    public async Task ActualCli_UsesRawValidation_AndStructureOnlyWorksWithoutDll()
    {
        var good = await CliHelpTests.Invoke(["validate", _root, "--structure-only"]);
        Assert.That(good.ExitCode, Is.Zero, good.Output);
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"type":"x","parameters":{}}]}""");
        var bad = await CliHelpTests.Invoke(["group", "validate", _root, "--file", Source, "--structure-only"]);
        Assert.That(bad.ExitCode, Is.EqualTo(4), bad.Output);
        Assert.That(bad.Output, Does.Contain("json.stringRequired").And.Contain("/entries/0/id"));
    }

    [Test]
    public void CompositeExpansion_MissingOutputSchema_IsLocatedAtSourceEntry()
    {
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"id":"bundle","type":"custom.bundle","parameters":{}}]}""");
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test", [],
            [new DefaultCompositeEntryBase("custom.bundle", DynamicParameterTable.Empty, () => new MissingOutputEntry())])]);
        var result = new ProjectValidationService(_root).Validate("group", new(File: Source), catalog: catalog);
        Assert.That(result.Diagnostics.Any(item => item.Code == "entry.compileInvalid" && item.ObjectId == "bundle" && item.Message.Contains("missing.output", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void AlternateGraph_UsesItsDirectory_AndInvalidVariableDoesNotGetAnInventedUid()
    {
        const string alternate = "Other/graph.json";
        Put(alternate, Graph.Replace("groups/a.rawgalgroup", "../Graph/groups/a.rawgalgroup", StringComparison.Ordinal));
        Assert.That(new ProjectValidationService(_root, alternate).Validate("graph").Success, Is.True);
        Put(alternate, Graph.Replace("\"playerVariables\":[]", "\"playerVariables\":[{\"name\":\"score\",\"defaultValue\":{\"Name\":\"score\",\"Type\":1,\"Value\":0}}]", StringComparison.Ordinal));
        var result = new ProjectValidationService(_root, alternate).Validate("graph", structureOnly: true);
        Assert.That(result.Diagnostics.Any(item => item.Code == "json.stringRequired" && item.JsonPointer == "/playerVariables/0/defaultValue/Uid"), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(_root, alternate)), Does.Not.Contain("Uid"));
    }

    [Test]
    public async Task ActualCli_UsesDeployedCustomCompositeContract()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var contract = Directory.CreateDirectory(Path.Combine(_root, "Contracts")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName, "test", "Fixtures", "SchemaA", "bin", configuration, "net10.0")))
            File.Copy(file, Path.Combine(contract, Path.GetFileName(file)));
        Put(Source, """{"version":2,"kind":"Raw","entries":[{"id":"bundle","type":"custom.bundle","parameters":{}}]}""");
        var result = await CliHelpTests.Invoke(["group", "validate", _root, "--id", "a"]);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        using var parsed = JsonDocument.Parse(result.Output);
        Assert.That(parsed.RootElement.GetProperty("data").GetProperty("contractSha256").GetString(), Has.Length.EqualTo(64));
    }

    private sealed class MissingOutputEntry : CompositeEntry
    {
        public override string Type => "custom.bundle";
        public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) =>
            [new PrimitiveEntry("missing.output", JsonSerializer.SerializeToElement(new { }))];
    }

    private static TargetProfileEntryCatalog Catalog()
    {
        var parameters = new DynamicParameterTable([
            new("value", typeof(int), isRequired: true),
            new("mode", typeof(string), constraints: JsonSerializer.SerializeToElement(new { options = new[] { "one", "two" } }))],
            arguments => { if (arguments.GetProperty("value").GetInt32() <= 0) throw new InvalidDataException("value must be positive"); });
        return new TargetProfileEntryCatalog([new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("custom.test", parameters)])]);
    }
    private void Put(string file, string text)
    { var path = Path.Combine(_root, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
    private string[] Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order().Select(file => file + ":" + File.ReadAllText(file)).ToArray();
}
