using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public class TargetProfileEntryCatalogTests
{
    [Test]
    public void EditorDefaultsPreserveStringEncodedTypesAndDoNotCoerceNullStrings()
    {
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.defaults", new([
            new("day", new GalNet.Core.Parameters.ValueDescriptor(typeof(DayOfWeek), jsonOptions: options), defaultValue: JsonSerializer.SerializeToElement("Monday")),
            new("name", typeof(string), defaultValue: JsonSerializer.SerializeToElement<string?>(null), allowNull: true)]))])]);
        var entry = catalog.Create("custom.defaults");
        Assert.That(entry.Arguments.GetProperty("day").GetString(), Is.EqualTo("Monday"));
        Assert.That(entry.Arguments.GetProperty("name").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(catalog.Get("custom.defaults").DynamicParameters["name"].DefaultValue!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
    }
    [Test]
    public void ProfileExposesOnlyItsOwnPrimitiveEntries()
    {
        var parameters = new DynamicParameterTable(
        [
            new DynamicParameterDescriptor("count", typeof(int), isRequired: true),
            new DynamicParameterDescriptor("enabled", typeof(bool), defaultValue: JsonSerializer.SerializeToElement(true))
        ]);
        var catalog = new TargetProfileEntryCatalog([new TestEntryModule("custom", [Primitive("custom.pulse", parameters)])]);

        Assert.That(catalog.TryGet("custom.pulse", out var definition), Is.True);
        Assert.That(definition.Kind, Is.EqualTo(EntryKind.Primitive));
        Assert.That(definition.DynamicParameters["count"].ValueType, Is.EqualTo(typeof(int)));
        Assert.That(definition.DynamicParameters["enabled"].DefaultValue!.Value.GetBoolean(), Is.True);
        Assert.That(catalog.TryGet("layer.show", out _), Is.False);
    }

    [Test]
    public void CompilerUsesTheSelectedProfileAndPreservesJsonArguments()
    {
        var catalog = new TargetProfileEntryCatalog(
        [
            new TestEntryModule("custom",
            [Primitive("custom.pulse", new DynamicParameterTable(
                [new DynamicParameterDescriptor("payload", typeof(JsonElement), isRequired: true)]))])
        ]);
        var document = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries =
            [
                new GroupEntryDocument
                {
                    Id = "pulse-1",
                    Type = "custom.pulse",
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["payload"] = JsonSerializer.SerializeToElement(new { enabled = true, count = 3 })
                    }
                }
            ]
        };

        var compiled = GalgroupCompiler.Compile(document, catalog).Document;

        Assert.That(compiled.Entries.Single().TypeId, Is.EqualTo("custom.pulse"));
        Assert.That(compiled.Entries.Single().Arguments.GetProperty("payload").GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(compiled.Entries.Single().Arguments.GetProperty("payload").GetProperty("count").GetInt32(), Is.EqualTo(3));
        Assert.That(compiled.Entries.Single().Arguments.GetRawText(), Does.Not.Contain("System.Text.Json.JsonElement"));
        var disabledDocument = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries = [new GroupEntryDocument { Id = "disabled", Type = "layer.show" }]
        };
        Assert.That(() => GalgroupCompiler.Compile(disabledDocument, catalog),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("not enabled"));
    }

    [Test]
    public void CompilerRejectsMissingRequiredPrimitiveParameter()
    {
        var catalog = new TargetProfileEntryCatalog(
        [new TestEntryModule("custom", [Primitive("custom.pulse", new DynamicParameterTable(
            [new DynamicParameterDescriptor("count", typeof(int), isRequired: true)]))])]);
        var document = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries = [new GroupEntryDocument { Id = "pulse-1", Type = "custom.pulse" }]
        };

        Assert.That(() => GalgroupCompiler.Compile(document, catalog),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("requires parameter 'count'"));
    }

    private static DefaultPrimitiveEntryBase Primitive(string type, DynamicParameterTable parameters) =>
        new DefaultPrimitiveEntryBase(type, parameters);

    private sealed class TestEntryModule(string id, IEnumerable<PrimitiveEntryBase> entries)
        : EntryModuleBase(id, entries);
}
