using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public class TargetProfileEntryCatalogTests
{
    [Test]
    public void ProfileExposesOnlyItsOwnPrimitiveDescriptors()
    {
        var descriptor = new PrimitiveDescriptor(
            "custom.pulse",
            [
                new PrimitiveParameterDescriptor("count", PrimitiveParameterKind.WholeNumber, IsRequired: true),
                new PrimitiveParameterDescriptor("enabled", PrimitiveParameterKind.Flag, DefaultValue: JsonSerializer.SerializeToElement(true))
            ]);
        var catalog = new TargetProfileEntryCatalog([descriptor]);

        Assert.That(catalog.TryGet("custom.pulse", out var definition), Is.True);
        Assert.That(definition.Kind, Is.EqualTo(EntryKind.Primitive));
        Assert.That(definition.Parameters["count"], Is.EqualTo(EntryParameterType.Integer));
        Assert.That(definition.Defaults["enabled"], Is.EqualTo("true"));
        Assert.That(catalog.TryGet("layer.show", out _), Is.False);
    }

    [Test]
    public void CompilerUsesTheSelectedProfileAndPreservesJsonArguments()
    {
        var catalog = new TargetProfileEntryCatalog(
        [
            new PrimitiveDescriptor(
                "custom.pulse",
                [new PrimitiveParameterDescriptor("payload", PrimitiveParameterKind.JsonObject, IsRequired: true)])
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
        [new PrimitiveDescriptor("custom.pulse", [new PrimitiveParameterDescriptor("count", PrimitiveParameterKind.WholeNumber, IsRequired: true)])]);
        var document = new GroupDocument
        {
            Kind = GroupDocumentKind.Raw,
            Entries = [new GroupEntryDocument { Id = "pulse-1", Type = "custom.pulse" }]
        };

        Assert.That(() => GalgroupCompiler.Compile(document, catalog),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("requires parameter 'count'"));
    }

    [Test]
    public void ProfileCanReadTheSameDescriptorsMountedByModules()
    {
        using var module = new DescriptorOnlyModule("custom", new PrimitiveDescriptor("custom.pulse", []));

        var catalog = TargetProfileEntryCatalog.FromModules([module]);

        Assert.That(catalog.TryGet("custom.pulse", out _), Is.True);
    }

    private sealed class DescriptorOnlyModule(string prefix, params PrimitiveDescriptor[] descriptors) : IPrimitiveModule
    {
        public string Prefix { get; } = prefix;
        public IReadOnlyCollection<PrimitiveDescriptor> Descriptors { get; } = descriptors;

        public PrimitiveDispatch Dispatch(string command, PrimitiveContext context, JsonElement arguments, PrimitiveExecutionControl control, CancellationToken cancellationToken) =>
            new(PrimitiveDispatchStatus.Skipped, new PrimitiveExecutionPolicy(false, false, null), Task.FromResult(PrimitiveResult.Empty));

        public void Dispose() { }
    }
}
