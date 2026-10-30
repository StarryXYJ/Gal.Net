using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using GalNet.Core.Configuration;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ProjectContentContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public static IEnumerable<TestCaseData> Fields() => Leaves(ToJson(CreateDocument()), [])
        // These discriminators have dedicated tests: they cannot change independently of their payload.
        .Where(path => path is not ["graph", "playerVariables" or "saveVariables", _, "type"] &&
            path is not ["graph", "playerVariables" or "saveVariables", _, "defaultValue", "Type"] &&
            string.Join('/', path) != "configuration/version")
        .Select(path => new TestCaseData((object)path).SetName("ContentField/" + string.Join('/', path)));

    [TestCaseSource(nameof(Fields))]
    public void EverySerializedFieldAffectsComparisonAndSurvivesCapture(string[] path)
    {
        var original = ToJson(CreateDocument());
        var changed = original.DeepClone();
        var parent = At(changed, path[..^1]);
        var value = At(changed, path);
        var replacement = path is ["files", _, "data"]
            ? JsonValue.Create(Convert.ToBase64String([2, 3]))
            : ChangedValue(value);
        Set(parent, path[^1], replacement);
        var document = changed.Deserialize<EditorProjectDocument>(Json)!;
        var baseline = Baseline(original);
        var displayNumber = path is ["groupEntries", _, _, "id"];
        Assert.That(baseline.Matches(document), Is.EqualTo(displayNumber), "Only displayed entry numbers are excluded.");
        Assert.That(JsonNode.DeepEquals(ToJson(document), changed), Is.True, "The mutation must survive deserialization.");
        Assert.That(JsonNode.DeepEquals(ToJson(EditorDocumentCloner.Clone(document)), changed), Is.True,
            "Capture must retain the changed field.");
        Assert.That(JsonNode.DeepEquals(changed, ToJson(document)), Is.True, "Capture must not mutate its source.");
        Set(parent, path[^1], value?.DeepClone());
        Assert.That(baseline.Matches(changed.Deserialize<EditorProjectDocument>(Json)!), Is.True, "Restoring the field must return to clean.");
    }

    [Test]
    public void FixturePopulatesEverySerializedPropertyIncludingOptionalFields()
    {
        RequirePopulated(CreateDocument(), "project");
    }

    [Test]
    public void ConfigurationEnvelopeVersionIsAFormatGateRatherThanEditableContent()
    {
        var json = ToJson(CreateDocument());
        json["configuration"]!["version"] = ProjectConfiguration.CurrentVersion + 1;
        Assert.Throws<JsonException>(() => json.Deserialize<EditorProjectDocument>(Json));
    }

    [Test]
    public void CapturedMutableContainersAreIndependentOfTheAuthorDocument()
    {
        var original = CreateDocument();
        var before = ToJson(original);
        var copy = EditorDocumentCloner.Clone(original);
        copy.Graph.Nodes[0].Options![0].Text = "changed";
        copy.Graph.Nodes[0].Conditions![0].Expression = "false";
        copy.Graph.Edges.Clear();
        copy.Graph.PlayerVariables[0].DefaultValue.Name = "changed";
        copy.Graph.PlayerVariables[0].DefaultValue.SetValue(42);
        copy.Graph.SaveVariables[0].Description = "changed";
        copy.GroupEntries["group"][0].Parameters.Clear();
        copy.Files["file"].Data[0]++;
        copy.Configuration.Remove("future");
        Assert.That(JsonNode.DeepEquals(ToJson(original), before), Is.True);
    }

    private static void RequirePopulated(object value, string path)
    {
        if (value is ProjectConfiguration configuration) { RequirePopulated(configuration.Sections, path + "/sections"); return; }
        if (value is IDictionary map)
        {
            Assert.That(map.Count, Is.GreaterThan(0), path);
            foreach (DictionaryEntry item in map) RequirePopulated(item.Value!, path + "/" + item.Key);
            return;
        }
        if (value is IEnumerable sequence and not string and not byte[])
        {
            Assert.That(sequence.Cast<object>(), Is.Not.Empty, path);
            foreach (var item in sequence) RequirePopulated(item, path + "/*");
            return;
        }
        foreach (var property in Json.GetTypeInfo(value.GetType()).Properties.Where(property => property.Get is not null))
        {
            var child = property.Get!(value);
            Assert.That(child, Is.Not.Null, $"Populate {path}/{property.Name} so the field receives a mutation test.");
            RequirePopulated(child!, path + "/" + property.Name);
        }
    }

    [TestCase("graph/nodes")]
    [TestCase("graph/edges")]
    [TestCase("graph/playerVariables")]
    [TestCase("graph/saveVariables")]
    [TestCase("graph/nodes/0/options")]
    [TestCase("graph/nodes/0/conditions")]
    [TestCase("groupEntries/group")]
    [TestCase("groupEntries")]
    [TestCase("files")]
    [TestCase("configuration/sections")]
    [TestCase("groupEntries/group/0/parameters")]
    public void CollectionMembershipIsContent(string location)
    {
        var original = ToJson(CreateDocument());
        var changed = original.DeepClone();
        var collection = At(changed, location.Split('/'));
        if (collection is JsonArray array) array.RemoveAt(0);
        else { var map = collection!.AsObject(); map.Remove(map.First().Key); }
        Assert.That(Baseline(original).Matches(changed.Deserialize<EditorProjectDocument>(Json)!), Is.False);
    }

    [TestCase("graph/nodes")]
    [TestCase("graph/edges")]
    [TestCase("graph/playerVariables")]
    [TestCase("graph/saveVariables")]
    [TestCase("graph/nodes/0/options")]
    [TestCase("graph/nodes/0/conditions")]
    [TestCase("groupEntries/group")]
    public void SequenceOrderIsContent(string location)
    {
        var json = ToJson(CreateDocument());
        var array = At(json, location.Split('/'))!.AsArray();
        var second = array[0]!.DeepClone();
        var field = Leaves(second, []).First();
        Set(At(second, field[..^1]), field[^1], ChangedValue(At(second, field)));
        array.Add(second);
        var baseline = Baseline(json);
        array.RemoveAt(1);
        array.Insert(0, second);
        Assert.That(baseline.Matches(json.Deserialize<EditorProjectDocument>(Json)!), Is.False);
    }

    [TestCase("groupEntries")]
    [TestCase("files")]
    [TestCase("configuration/sections")]
    [TestCase("groupEntries/group/0/parameters")]
    public void MapKeysMatterButTheirInsertionOrderDoesNot(string location)
    {
        var json = ToJson(CreateDocument());
        var map = At(json, location.Split('/'))!.AsObject();
        var first = map.First();
        map.Add("second", first.Value!.DeepClone());
        var baseline = Baseline(json);
        map.Remove(first.Key);
        map.Add(first.Key, first.Value);
        Assert.That(baseline.Matches(json.Deserialize<EditorProjectDocument>(Json)!), Is.True);
        map.Remove(first.Key);
        map.Add("renamed", first.Value);
        Assert.That(baseline.Matches(json.Deserialize<EditorProjectDocument>(Json)!), Is.False);
    }

    [TestCase(VariableType.Bool)]
    [TestCase(VariableType.Int)]
    [TestCase(VariableType.Float)]
    [TestCase(VariableType.String)]
    public void VariableTypeAndValueBothParticipate(VariableType type)
    {
        var document = CreateDocument();
        var definition = document.Graph.PlayerVariables[0];
        definition.Type = type;
        var baseline = Baseline(ToJson(document));
        definition.DefaultValue.Value = type switch
        {
            VariableType.Bool => VariableValue.From(true),
            VariableType.Int => VariableValue.From(37),
            VariableType.Float => VariableValue.From(1.25f),
            _ => VariableValue.From("changed")
        };
        Assert.That(baseline.Matches(document), Is.False);
        Assert.That(JsonNode.DeepEquals(ToJson(document), ToJson(EditorDocumentCloner.Clone(document))), Is.True);
        definition.Type = type == VariableType.String ? VariableType.Bool : VariableType.String;
        Assert.That(baseline.Matches(document), Is.False);
    }

    [Test]
    public void DeletedFileBytesAreIgnoredButResurrectionIsDirty()
    {
        var document = CreateDocument();
        document.Files["file"].Exists = false;
        var baseline = Baseline(ToJson(document));
        document.Files["file"].Data[0]++;
        Assert.That(baseline.Matches(document), Is.True);
        document.Files["file"].Exists = true;
        Assert.That(baseline.Matches(document), Is.False);
    }

    [TestCase("{\"nested\":{\"array\":[null,9007199254740993]}}", "{\"nested\":{\"array\":[null,9007199254740994]}}")]
    [TestCase("{\"value\":null}", "{}")]
    [TestCase("{\"value\":1}", "{\"value\":\"1\"}")]
    [TestCase("{\"items\":[1,2]}", "{\"items\":[2,1]}")]
    public void UnknownJsonRetainsItsStructureAndPrecision(string before, string after)
    {
        foreach (var configuration in new[] { false, true })
        {
            var document = CreateDocument();
            void Assign(string json)
            {
                var value = JsonSerializer.Deserialize<JsonElement>(json);
                if (configuration) document.Configuration.SetRaw("future", new(1, value));
                else document.GroupEntries["group"][0].Parameters["future"] = value;
            }
            Assign(before);
            var baseline = Baseline(ToJson(document));
            Assign(after);
            Assert.That(baseline.Matches(document), Is.False);
            Assign(before);
            Assert.That(baseline.Matches(document), Is.True);
        }
    }

    private static ProjectContentBaseline Baseline(JsonNode node)
    {
        var baseline = new ProjectContentBaseline();
        baseline.Set(JsonSerializer.SerializeToUtf8Bytes(node, Json));
        return baseline;
    }

    private static JsonNode ToJson(EditorProjectDocument document) => JsonSerializer.SerializeToNode(document, Json)!;
    private static JsonNode? At(JsonNode? node, IEnumerable<string> path)
    {
        foreach (var key in path) node = node is JsonArray array ? array[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)] : node![key];
        return node;
    }
    private static void Set(JsonNode? parent, string key, JsonNode? value)
    {
        if (parent is JsonArray array) array[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)] = value;
        else parent![key] = value;
    }
    private static IEnumerable<string[]> Leaves(JsonNode? node, string[] path)
    {
        if (node is JsonObject map)
            foreach (var (key, child) in map) { foreach (var leaf in Leaves(child, [..path, key])) yield return leaf; }
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++) { foreach (var leaf in Leaves(array[i], [..path, i.ToString(System.Globalization.CultureInfo.InvariantCulture)])) yield return leaf; }
        else yield return path;
    }
    private static JsonValue ChangedValue(JsonNode? node) => node?.GetValueKind() switch
    {
        JsonValueKind.String => JsonValue.Create(node.GetValue<string>() + "_changed")!,
        JsonValueKind.Number => JsonValue.Create(node.GetValue<double>() + 1)!,
        JsonValueKind.True or JsonValueKind.False => JsonValue.Create(!node.GetValue<bool>())!,
        JsonValueKind.Null or null => JsonValue.Create("provided")!,
        _ => throw new InvalidOperationException("Add a field mutation for " + node)
    };

    private static EditorProjectDocument CreateDocument()
    {
        var document = new EditorProjectDocument
        {
            Graph = new()
            {
                Name = "Author", RootNodeId = "group",
                Nodes = [new()
                {
                    Id = "group", Type = "Group", Name = "Group", X = 1.5, Y = 2.5, File = "groups/a.rawgalgroup",
                    StoryPackage = "chapter", BranchType = "choice",
                    Options = [new() { Id = "option", Text = "Choice", Condition = "true" }],
                    Conditions = [new() { Id = "condition", Expression = "true" }]
                }],
                Edges = [new() { Id = "edge", FromNodeId = "group", FromOutlet = 0, ToNodeId = "next" }],
                PlayerVariables = [Definition("player")], SaveVariables = [Definition("save")]
            },
            GroupEntries = new() { ["group"] = [new() { StableId = "entry", Id = 1, Type = "wait", Condition = "true",
                Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1), ["future"] = JsonSerializer.SerializeToElement(new { text = "unknown", enabled = true }) } }] },
            Files = new() { ["file"] = new() { Id = "file", Path = "notes.txt", Exists = true, ManagedContent = true, Data = [1, 2] } }
        };
        document.Configuration.SetRaw("future", new(1, JsonSerializer.SerializeToElement(new { text = "unknown", count = 1 })));
        return document;
    }

    private static ProjectVariableDefinition Definition(string name) => new()
    {
        Name = name, Description = "description",
        DefaultValue = new() { Uid = name, Name = name, Value = VariableValue.From(0) }
    };
}
