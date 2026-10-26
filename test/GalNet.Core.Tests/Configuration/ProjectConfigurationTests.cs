using System.Text.Json;
using GalNet.Core.Configuration;
using GalNet.Core.Parameters;

namespace GeneralTest.Configuration;

public sealed class ProjectConfigurationTests
{
    private static readonly ConfigurationSection<Options> Section = new("custom.example",
    [new("count", new ValueDescriptor(typeof(int), constraints: JsonSerializer.SerializeToElement(new { minimum = 1 })),
        isRequired: true, defaultValue: JsonSerializer.SerializeToElement(3)),
     new("names", new ValueDescriptor(typeof(List<string>), allowNull: false))]);

    [Test]
    public void DefaultsAndReadsAreDetachedAndDoNotPersistImplicitly()
    {
        var configuration = new ProjectConfiguration();
        var options = configuration.Read(Section);
        Assert.That(options.Count, Is.EqualTo(3));
        options.Names.Add("caller");
        Assert.That(configuration.Sections, Is.Empty);
        Assert.That(configuration.Read(Section).Names, Is.Empty);
        configuration.Set(Section, options);
        options.Names.Clear();
        var read = configuration.Read(Section);
        read.Names.Add("local");
        Assert.That(configuration.Read(Section).Names, Is.EqualTo(new[] { "caller" }));
    }

    [Test]
    public void TypedWritesPreserveUnknownSectionsAndFieldsAndOwnTheirJson()
    {
        var configuration = new ProjectConfiguration();
        using (var json = JsonDocument.Parse("""{"count":4,"future":{"items":[9007199254740993,null]}}"""))
            configuration.SetRaw(Section.Id, new(1, json.RootElement));
        using (var json = JsonDocument.Parse("""{"enabled":true}"""))
            configuration.SetRaw("unknown", new(7, json.RootElement));
        var options = configuration.Read(Section);
        options.Count = 5;
        configuration.Set(Section, options);
        var clone = configuration.Clone();
        configuration.Remove("unknown");
        var restored = JsonSerializer.Deserialize<ProjectConfiguration>(JsonSerializer.Serialize(clone))!;
        Assert.That(restored.Read(Section).Count, Is.EqualTo(5));
        Assert.That(restored.Sections[Section.Id].Data.GetProperty("future").GetProperty("items")[0].GetInt64(), Is.EqualTo(9007199254740993L));
        Assert.That(restored.Sections["unknown"].Version, Is.EqualTo(7));
        Assert.That(restored.Sections["unknown"].Data.GetProperty("enabled").GetBoolean(), Is.True);
    }

    [TestCase("{}")]
    [TestCase("{\"version\":2,\"sections\":{}}")]
    [TestCase("{\"version\":\"1\",\"sections\":{}}")]
    [TestCase("{\"version\":1,\"sections\":{},\"sections\":{}}")]
    [TestCase("{\"version\":1,\"sections\":{\"a\":{\"version\":\"1\",\"data\":{}}}}")]
    [TestCase("{\"version\":1,\"sections\":{\"a\":{\"version\":1,\"data\":{\"nested\":{\"x\":1,\"x\":2}}}}}")]
    public void InvalidEnvelopeOrDuplicateMembersAreRejected(string json) =>
        Assert.That(() => JsonSerializer.Deserialize<ProjectConfiguration>(json), Throws.TypeOf<JsonException>());

    [TestCase("{\"count\":\"invalid\"}")]
    [TestCase("{\"count\":0}")]
    [TestCase("{\"count\":2,\"names\":null}")]
    public void SchemaRejectsKnownInvalidFieldsWithoutInterpretingUnknownSections(string data)
    {
        var configuration = new ProjectConfiguration();
        configuration.SetRaw("unknown", new(17, JsonSerializer.SerializeToElement(new { count = "opaque" })));
        var schema = new ProjectConfigurationSchema([Section]);
        Assert.That(() => schema.Validate(configuration), Throws.Nothing);
        configuration.SetRaw(Section.Id, new(1, JsonSerializer.Deserialize<JsonElement>(data)));
        Assert.That(() => schema.Validate(configuration), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void UnknownFieldCasingDoesNotBypassDeclaredFieldValidation()
    {
        var configuration = new ProjectConfiguration();
        configuration.SetRaw(Section.Id, new(1, JsonSerializer.Deserialize<JsonElement>("""{"count":4,"Count":0}""")));
        Assert.That(configuration.Read(Section).Count, Is.EqualTo(4));
        configuration.Set(Section, new() { Count = 5 });
        Assert.That(configuration.Read(Section).Count, Is.EqualTo(5));
        Assert.That(configuration.Sections[Section.Id].Data.GetProperty("Count").GetInt32(), Is.Zero);
    }

    [Test]
    public void RejectedUpdateRetainsOldValuesAndValidUpdateCanRepairInvalidKnownData()
    {
        var configuration = new ProjectConfiguration();
        configuration.Set(Section, new() { Count = 4 });
        Assert.That(() => configuration.Set(Section, new() { Count = 0 }), Throws.TypeOf<InvalidDataException>());
        Assert.That(configuration.Read(Section).Count, Is.EqualTo(4));
        configuration.SetRaw(Section.Id, new(1, JsonSerializer.Deserialize<JsonElement>("""{"count":0,"future":true}""")));
        configuration.Set(Section, new() { Count = 8 });
        Assert.That(configuration.Read(Section).Count, Is.EqualTo(8));
        Assert.That(configuration.Sections[Section.Id].Data.GetProperty("future").GetBoolean(), Is.True);
        configuration.SetRaw(Section.Id, new(2, JsonSerializer.SerializeToElement(new { count = 5 })));
        Assert.That(() => configuration.Set(Section, new() { Count = 6 }), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void DtoSchemaDefaultsUseFieldCodecsWithoutLeakingMetadata()
    {
        var section = ConfigurationSection.ForDto<DtoOptions>("custom.dto");
        var configuration = new ProjectConfiguration();
        Assert.That(configuration.Read(section).Count, Is.EqualTo(9));
        configuration.Set(section, configuration.Read(section));
        Assert.That(configuration.Sections[section.Id].Data.EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "count" }));
        var required = new ConfigurationSection<Options>("required", [new("count", new ValueDescriptor(typeof(int)), isRequired: true)]);
        Assert.That(() => required.Read(new(1, JsonSerializer.Deserialize<JsonElement>("{}"))), Throws.TypeOf<InvalidDataException>());
    }

    public sealed class Options
    {
        public int Count { get; set; } = 1;
        public List<string> Names { get; set; } = [];
    }

    public sealed class DtoOptions : IFieldDto, IFieldSchema<DtoOptions>
    {
        public int Count { get; set; }
        public static IReadOnlyList<FieldDescriptor> Schema { get; } =
            [new("count", new ValueDescriptor(typeof(int)), defaultValue: JsonSerializer.SerializeToElement(9))];
        public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => Count;
        public void WriteField(string id, object? value) => Count = (int)value!;
    }
}
