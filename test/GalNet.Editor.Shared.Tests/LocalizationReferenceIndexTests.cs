using GalNet.Editor.Abstraction.Settings;
using System.Text;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed class LocalizationReferenceIndexTests
{
    private static TargetProfileEntryCatalog Builtins() => new(BuiltinSchemas.CreateAllModules());
    private static EditorEntryData Entry(string id, string type, object parameters) => new()
    { StableId = id, Type = type, Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(parameters))! };
    private static LocalizationSource[] Sources =>
    [
        new("I18n/catalog/characters.json", """[{"id":"1","displayNameKey":"character.1"},{"id":"2","displayNameKey":"character.2"}]"""),
        new("I18n/zh-CN/base.json", """{"character.1":"同名","character.2":"同名","line":"正文","branch":"选项"}""")
    ];

    [Test]
    public void BuiltinAndCompositeSchemasExposeValueSemanticsWithoutChangingStringCodec()
    {
        foreach (var fields in new[] { DialogueTextOptions.Schema, TextVoiceOptions.Schema })
        {
            Assert.That(fields.Single(field => field.Id == "speaker").Semantic, Is.EqualTo(FieldValueSemantic.CharacterReference));
            Assert.That(fields.Single(field => field.Id == "content").Semantic, Is.EqualTo(FieldValueSemantic.LocalizedText));
            Assert.That(ValueCodec.ParseEditorText(fields.Single(field => field.Id == "content").Value, "line").GetString(), Is.EqualTo("line"));
        }
        Assert.That(NvlAppendOptions.Schema[0].Semantic, Is.EqualTo(FieldValueSemantic.LocalizedText));
        Assert.That(NvlVoiceOptions.Schema[0].Semantic, Is.EqualTo(FieldValueSemantic.LocalizedText));
        Assert.That(() => new FieldDescriptor("bad", new(typeof(int)), semantic: FieldValueSemantic.LocalizedText), Throws.ArgumentException);
    }

    [Test]
    public void AllGroupsCharactersAndChoiceOptionsAreIndexedWithoutMutatingOrRenumbering()
    {
        var document = new EditorProjectDocument
        {
            GroupEntries = new() { ["unopened"] = [Entry("stable", "dialogue.text", new { speaker = "1", content = "line" })] },
            Graph = new() { Nodes = [new() { Id = "choice", Type = "Branch", BranchType = "Choice", Options = [new() { Id = "option", Text = "branch" }] }] }
        };
        var original = JsonSerializer.Serialize(document);
        var index = new LocalizationReferenceIndex(document, Builtins(), Sources);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.Issues, Is.Empty);
        Assert.That(index.Characters, Has.Count.EqualTo(2));
        Assert.That(index.References.Single(reference => reference.Owner == "unopened" && reference.JsonPointer == "/content").EntryId, Is.EqualTo("stable"));
        Assert.That(index.References.Single(reference => reference.Owner == "choice").EntryId, Is.EqualTo("option"));
        Assert.That(JsonSerializer.Serialize(document), Is.EqualTo(original));
        document.GroupEntries["unopened"].Insert(0, Entry("other", "dialogue.text", new { speaker = "2", content = "line" }));
        var reordered = new LocalizationReferenceIndex(document, Builtins(), Sources);
        Assert.That(reordered.References.Single(reference => reference.EntryId == "stable" && reference.JsonPointer == "/content").Key, Is.EqualTo("line"));
    }

    [Test]
    public void SourcesPreserveOrdinalOverrideProvenanceEmptyTextAndExcludeExternal()
    {
        var sources = new[]
        {
            new LocalizationSource("I18n/zh-CN/z.json", """{"a.b":""}"""),
            new LocalizationSource("I18n/External/zh-CN/override.json", "broken"),
            new LocalizationSource("I18n/zh-CN/a.json", """{"a":{"b":"基础"}}"""),
            new LocalizationSource("I18n/en-US/base.json", """{"other":"English"}""")
        };
        var index = new LocalizationReferenceIndex(new(), Builtins(), sources);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.Texts, Has.Count.EqualTo(3));
        Assert.That(index.FindDirect("zh-cn", "a.b")!.Text, Is.Empty);
        Assert.That(index.FindDirect("zh-CN", "a.b")!.File, Does.EndWith("/z.json"));
        Assert.That(index.FindDirect("zh-CN", "other"), Is.Null, "Direct lookup must not disguise fallback as translation.");
        Assert.That(index.Texts.First().JsonPointer, Is.EqualTo("/other"));
    }

    [TestCase("{broken", "source.invalid")]
    [TestCase("{\"a\":1}", "text.stringRequired")]
    [TestCase("{\"a\":\"x\",\"a\":\"y\"}", "text.keyCollision")]
    [TestCase("{\"a.b\":\"x\",\"a\":{\"b\":\"y\"}}", "text.keyCollision")]
    public void InvalidSourcesDiagnoseIncompleteAnalysis(string json, string code)
    {
        var index = new LocalizationReferenceIndex(new(), Builtins(), [new("I18n/zh-CN/bad.json", json)]);
        Assert.That(index.Complete, Is.False);
        Assert.That(index.Issues.Any(issue => issue.Code == code && issue.File.EndsWith("bad.json", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void BrokenCatalogAndSourceDoNotHideOtherValidFiles()
    {
        var index = new LocalizationReferenceIndex(new(), Builtins(),
        [
            new("I18n/catalog/characters.json", """[{"id":"same","displayNameKey":"name"},{"id":"same","displayNameKey":"other"}]"""),
            new("I18n/zh-CN/bad.json", "[]"),
            new("I18n/zh-CN/valid.json", """{"name":"名字"}""")
        ]);
        Assert.That(index.Complete, Is.False);
        Assert.That(index.Issues.Select(issue => issue.Code), Does.Contain("catalog.invalid").And.Contain("text.objectRequired"));
        Assert.That(index.FindDirect("zh-CN", "name")!.Text, Is.EqualTo("名字"));
    }

    [Test]
    public void MissingSchemaAndGroupCannotBeInterpretedAsUnusedCharacters()
    {
        var document = new EditorProjectDocument
        {
            Graph = new() { Nodes = [new() { Id = "absent", Type = "Group" }] },
            GroupEntries = new() { ["unknown"] = [Entry("id", "custom.unknown", new { speaker = "1" })] }
        };
        var index = new LocalizationReferenceIndex(document, Builtins(), Sources);
        Assert.That(index.Complete, Is.False);
        Assert.That(index.Issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "group.missing", "schema.unavailable" }));
    }

    [Test]
    public void UnknownCharacterAndMissingDefaultTranslationAreReportedWithoutInventingText()
    {
        var document = new EditorProjectDocument { GroupEntries = new() { ["g"] = [Entry("id", "dialogue.text", new { speaker = "unknown", content = "missing" })] } };
        var index = new LocalizationReferenceIndex(document, Builtins(), []);
        Assert.That(index.Complete, Is.True, "All reference locations were analyzed even though their targets are invalid.");
        Assert.That(index.Issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "character.unknown", "text.defaultMissing" }));
    }

    [Test]
    public void NullAndAbsentFieldsRemainAbsentWhileUnknownFieldsAndInvalidKeysAreDiagnosed()
    {
        var entry = Entry("id", "dialogue.text", new { speaker = (string?)null, content = (string?)null });
        var document = new EditorProjectDocument { GroupEntries = new() { ["g"] = [entry] } };
        var index = new LocalizationReferenceIndex(document, Builtins(), []);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.References, Is.Empty);
        Assert.That(entry.Parameters["content"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        entry.Parameters.Remove("content");
        Assert.That(new LocalizationReferenceIndex(document, Builtins(), []).References, Is.Empty);
        entry.Parameters["content"] = JsonSerializer.SerializeToElement(42);
        entry.Parameters["extension"] = JsonSerializer.SerializeToElement("arbitrary");
        var invalid = new LocalizationReferenceIndex(document, Builtins(), []);
        Assert.That(invalid.Complete, Is.False);
        Assert.That(invalid.Issues.Select(issue => issue.Code), Is.EquivalentTo(new[] { "field.unknown", "field.stringRequired" }));
    }

    [Test]
    public void NestedListsUseStoredKeysAndEscapedPointersRatherThanTextOrArrayIdentity()
    {
        var fields = new DynamicParameterTable([new(new FieldDescriptor("items", new(typeof(List<NestedText>))))]);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.line", fields)])]);
        var document = new EditorProjectDocument { GroupEntries = new() { ["g"] = [Entry("id", "custom.line", new { items = new[] { new Dictionary<string, object?> { ["caption/~"] = "first" }, new Dictionary<string, object?> { ["caption/~"] = "second" } } })] } };
        var source = new LocalizationSource("I18n/zh-CN/base.json", """{"first":"同一句","second":"同一句"}""");
        var index = new LocalizationReferenceIndex(document, catalog, [source]);
        Assert.That(index.Complete, Is.True);
        Assert.That(index.References.Select(reference => reference.Key), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(index.References[0].JsonPointer, Is.EqualTo("/items/0/caption~1~0"));
    }

    [TestCase("null")]
    [TestCase("\"\"")]
    [TestCase("\"   \"")]
    [TestCase("{}")]
    [TestCase("[]")]
    public void EmptyValuesDoNotBlockCharacterReferenceAnalysis(string json)
    {
        var empty = JsonSerializer.Deserialize<JsonElement>(json);
        var layer = Entry("layer", "layer.show", new { });
        layer.Parameters["transform"] = empty;
        layer.Parameters["extension"] = empty;
        var dialogue = Entry("line", "dialogue.text", new { speaker = " \t", content = " \n" });
        var unknown = Entry("unknown", "custom.unknown", new { });
        unknown.Parameters["extension"] = empty;
        var document = new EditorProjectDocument { GroupEntries = new() { ["opening"] = [layer, dialogue, unknown] } };
        var index = new LocalizationReferenceIndex(document, Builtins(), []);
        Assert.Multiple(() =>
        {
            Assert.That(index.Complete, Is.True);
            Assert.That(index.Issues, Is.Empty);
            Assert.That(index.References, Is.Empty);
            Assert.That(layer.Parameters["transform"].GetRawText(), Is.EqualTo(empty.GetRawText()));
        });

        layer.Parameters["transform"] = JsonSerializer.SerializeToElement(new { x = 1 });
        var nonempty = new LocalizationReferenceIndex(document, Builtins(), []);
        Assert.That(nonempty.IncompleteIssues.Single().Code, Is.EqualTo("field.opaque"));
    }

    [Test]
    public void RecursiveDtosStopAtDepthBudgetAndRetainDiagnostics()
    {
        var fields = new DynamicParameterTable([new(new FieldDescriptor("root", new(typeof(NestedText))))]);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.tree", fields)])]);
        var nested = "{}";
        for (var i = 0; i < 25; i++) nested = "{\"child\":" + nested + "}";
        var entry = Entry("id", "custom.tree", new { });
        entry.Parameters["root"] = JsonDocument.Parse(nested).RootElement.Clone();
        var index = new LocalizationReferenceIndex(new() { GroupEntries = new() { ["g"] = [entry] } }, catalog, []);
        Assert.That(index.Complete, Is.False);
        Assert.That(index.Issues.Any(issue => issue.Code == "analysis.limit"), Is.True);
    }

    public sealed class NestedText : IFieldDto
    {
        public static IReadOnlyList<FieldDescriptor> Schema { get; } =
        [new("caption/~", new(typeof(string)), semantic: FieldValueSemantic.LocalizedText), new("child", new(typeof(NestedText)))];
        public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => throw new NotSupportedException();
        public void WriteField(string id, object? value) => throw new NotSupportedException();
    }

    [TestCase(false, "/items/0/Speaker")]
    [TestCase(true, "/items/0/speaker")]
    public void NestedRoleFieldsUseActualSerializerWireNames(bool camelCase, string pointer)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null };
        var table = new DynamicParameterTable([new(new FieldDescriptor("items", new(typeof(List<NestedRole>), jsonOptions: options)))]);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.roles", table)])]);
        var entry = Entry("id", "custom.roles", new { });
        entry.Parameters["items"] = JsonSerializer.SerializeToElement(new[] { new NestedRole { Speaker = "hero" } }, options);
        var index = new LocalizationReferenceIndex(new() { GroupEntries = new() { ["g"] = [entry] } }, catalog, []);
        Assert.That(index.Complete, Is.True); Assert.That(index.References.Single().JsonPointer, Is.EqualTo(pointer));
        Assert.That(index.References.Single().Semantic, Is.EqualTo(FieldValueSemantic.CharacterReference));
    }

    public sealed class NestedRole : IFieldDto
    {
        [Field(0), CharacterReference] public string Speaker { get; set; } = "";
        public static IReadOnlyList<FieldDescriptor> Schema { get; } = [new("speaker", new(typeof(string)), semantic: FieldValueSemantic.CharacterReference)];
        [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => Speaker;
        public void WriteField(string id, object? value) => Speaker = (string)value!;
    }

    [TestCase(false, "/items/0/Member/Speaker")]
    [TestCase(true, "/items/0/member/speaker")]
    public void NestedWireOptionsPropagateThroughMultipleDtoLevels(bool camelCase, string pointer)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null };
        var table = new DynamicParameterTable([new(new FieldDescriptor("items", new(typeof(List<NestedRoleContainer>), jsonOptions: options)))]);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.roles", table)])]);
        var entry = Entry("id", "custom.roles", new { });
        entry.Parameters["items"] = JsonSerializer.SerializeToElement(new[] { new NestedRoleContainer { Member = new() { Speaker = "hero" } } }, options);
        var index = new LocalizationReferenceIndex(new() { GroupEntries = new() { ["g"] = [entry] } }, catalog, []);
        Assert.That(index.Complete, Is.True); Assert.That(index.References.Single().JsonPointer, Is.EqualTo(pointer));
    }

    public sealed class NestedRoleContainer : IFieldDto
    {
        [Field(0)] public NestedRole Member { get; set; } = new();
        public static IReadOnlyList<FieldDescriptor> Schema { get; } = [new("member", new(typeof(NestedRole)))];
        [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => Member;
        public void WriteField(string id, object? value) => Member = (NestedRole)value!;
    }

    [Test]
    public async Task ProjectQuerySeesUnsavedManagedFilesAndDisposalInvalidatesCachedSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), "GalNet-localization-" + Guid.NewGuid().ToString("N"));
        await EditorProjectCreator.CreateAsync(new(root, "Localization"));
        try
        {
            var catalog = Builtins();
            var repository = new EditorDocumentRepository(catalog);
            var settings = DirectProjectPersistence.LoadSettings(root);
            var documents = new EditorDocumentService();
            documents.Load(repository.Load(root, "Localization", settings));
            await using var project = new GalProject("id", "Localization", root, settings, new(), new EmptyScope());
            using var session = new ProjectContentSession(documents, repository);
            session.Open(project);
            using var query = new ProjectLocalizationQuery(session, documents, catalog, new ProjectStub(project));
            var path = "I18n/zh-CN/new.json";
            Directory.CreateDirectory(Path.Combine(root, "I18n", "zh-CN"));
            File.WriteAllText(Path.Combine(root, path), """{"draft":"disk"}""");
            Assert.That(query.Get()!.FindDirect("zh-CN", "draft")!.Text, Is.EqualTo("disk"));
            var file = session.TrackFile(path, Encoding.UTF8.GetBytes("""{"draft":"unsaved"}"""));
            file.Data = Encoding.UTF8.GetBytes("""{"draft":"unsaved"}""");
            var snapshot = query.Get()!;
            Assert.That(snapshot.FindDirect("zh-CN", "draft")!.Text, Is.EqualTo("unsaved"));
            Assert.That(query.Get(), Is.SameAs(snapshot));
            file.Exists = false;
            Assert.That(query.Get()!.FindDirect("zh-CN", "draft"), Is.Null);
            session.TrackFile(path, Encoding.UTF8.GetBytes("""{"draft":"replacement"}"""));
            Assert.That(query.Get()!.FindDirect("zh-CN", "draft")!.Text, Is.EqualTo("replacement"), "Deleted document identities must not hide a replacement at the same path.");
            var beforeUnload = query.Revision;
            documents.Unload();
            Assert.That(query.Get(), Is.Null);
            Assert.That(query.Revision, Is.GreaterThan(beforeUnload));
            documents.Load(repository.Load(root, "Localization", settings));
            Assert.That(query.Get(), Is.Not.Null);
            var revision = query.Revision;
            query.Dispose();
            Assert.That(query.Get(), Is.Null);
            Assert.That(query.Revision, Is.GreaterThan(revision));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ProjectStub(GalProject project) : IProjectService
    {
        public GalProject? Current => project;
        public event Action<GalProject?>? CurrentChanged { add { } remove { } }
        public Task<GalProject> OpenAsync(string projectPath) => throw new NotSupportedException();
        public Task<GalProject> CreateAsync(string projectPath, string name, ProjectSettings settings, string contractsDirectory) => throw new NotSupportedException();
        public Task CloseAsync() => throw new NotSupportedException();
        public Task SaveAsync() => throw new NotSupportedException();
        public IReadOnlyList<RecentProjectInfo> GetRecentProjects() => [];
        public void RemoveRecentProject(string projectPath) => throw new NotSupportedException();
        public Task<bool> CheckUnsavedChangesAsync() => throw new NotSupportedException();
    }

    private sealed class EmptyScope : IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => null;
        public void Dispose() { }
    }
}
