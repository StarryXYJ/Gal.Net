using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public class TargetProfileEntryCatalogTests
{
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
        Assert.That(definition.Parameters["count"], Is.EqualTo(EntryParameterType.Integer));
        Assert.That(definition.DynamicParameters!["count"].ValueType, Is.EqualTo(typeof(int)));
        Assert.That(definition.Defaults["enabled"], Is.EqualTo("true"));
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

    private static PrimitiveEntryBase Primitive(string type, DynamicParameterTable parameters) =>
        new DefaultPrimitiveEntryBase(type, parameters, static _ => new ImmediatePrimitiveInstance());

    private sealed class TestEntryModule(string id, IEnumerable<PrimitiveEntryBase> entries)
        : EntryModuleBase(id, entries);
}
