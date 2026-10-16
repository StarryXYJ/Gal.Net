using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public sealed class DtoExecutionTests
{
    [Test]
    public void DtoWritingPreservesFieldCodecsExplicitNullAndOptionalJsonOmission()
    {
        var data = new WriteOptions();
        var encoded = ValueCodec.SerializeFields(data);
        Assert.That(encoded.GetProperty("flag").GetString(), Is.EqualTo("true"));
        Assert.That(encoded.TryGetProperty("payload", out _), Is.False);
        data.Flag = null;
        Assert.That(ValueCodec.SerializeFields(data).GetProperty("flag").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(ValueCodec.DeserializeFields<WriteOptions>(data.Fields, ValueCodec.SerializeFields(data)).Flag, Is.Null);
        Assert.That(ValueCodec.SerializeFields(data, omitNullValues: true).TryGetProperty("flag", out _), Is.False);
        data.Payload = JsonSerializer.SerializeToElement(new { value = 3 });
        Assert.That(ValueCodec.SerializeFields(data).GetProperty("payload").GetProperty("value").GetInt32(), Is.EqualTo(3));
    }

    private sealed class WriteOptions : IFieldDto
    {
        public WriteOptions() { }
        public bool? Flag { get; set; } = true;
        public JsonElement Payload { get; set; }
        public IReadOnlyList<FieldDescriptor> Fields { get; } =
        [new("flag", new ValueDescriptor(typeof(bool?), allowNull: true,
            jsonOptions: new JsonSerializerOptions { Converters = { new StringBooleanValueConverter() } })),
         new("payload", new ValueDescriptor(typeof(JsonElement)))];
        public object? ReadField(string id) => id == "flag" ? Flag : Payload;
        public void WriteField(string id, object? value)
        { if (id == "flag") Flag = (bool?)value; else Payload = (JsonElement)value!; }
    }

    [Test]
    public void ExplicitStringCodecDescribesStringTypeAndChoicesInsteadOfAnOpaqueSchema()
    {
        var value = new ValueDescriptor(typeof(string), allowNull: true,
            jsonOptions: new JsonSerializerOptions { Converters = { new NamedStringConverter() } });
        var schema = ValueCodec.Describe(value).GetProperty("anyOf")[0];
        Assert.That(schema.GetProperty("allOf")[0].GetProperty("type").EnumerateArray().Select(item => item.GetString()),
            Is.EquivalentTo(new[] { "string", "null" }));
        Assert.That(schema.GetProperty("enum")[0].GetString(), Is.EqualTo("first"));
        Assert.That(ValueCodec.Deserialize(value, JsonSerializer.SerializeToElement("first")), Is.EqualTo("first"));
        Assert.That(ValueValidation.Validate(value, JsonSerializer.SerializeToElement(42)), Is.Not.Null);
    }

    private sealed class NamedStringConverter : JsonConverter<string>, IStringValueEncoding
    {
        public IReadOnlyList<string> Choices { get; } = Array.AsReadOnly(new[] { "first" });
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }

    [Test]
    public void StringBooleanKeepsWireChoicesAndEditorTextWhileReadingStrongType()
    {
        var value = new ValueDescriptor(typeof(bool?), allowNull: true,
            jsonOptions: new JsonSerializerOptions { Converters = { new StringBooleanValueConverter() } });
        var encoded = ValueCodec.Serialize(value, true);
        Assert.That(encoded.GetString(), Is.EqualTo("true"));
        Assert.That(ValueCodec.Deserialize(value, encoded), Is.EqualTo(true));
        Assert.That(ValueCodec.ToEditorText(value, encoded), Is.EqualTo("true"));
        Assert.That(ValueCodec.FromEditorText(value, "false").GetString(), Is.EqualTo("false"));
        Assert.That(ValueValidation.Validate(value, JsonSerializer.SerializeToElement(true))!.Code, Is.EqualTo("value.type"));
        Assert.That(ValueCodec.Deserialize(value, JsonSerializer.SerializeToElement<object?>(null)), Is.Null);
        Assert.That(ValueCodec.Describe(value).GetProperty("anyOf")[0].GetProperty("enum").EnumerateArray().Select(item => item.GetString()),
            Is.EqualTo(new[] { "true", "false" }));
    }

    [Test]
    public void StringEnumRetainsCaseAndRejectsNumericOrUndefinedValues()
    {
        var value = new ValueDescriptor(typeof(DayOfWeek?), allowNull: true,
            jsonOptions: new JsonSerializerOptions { Converters = { new StringEnumValueConverter<DayOfWeek>() } });
        Assert.That(ValueCodec.Serialize(value, DayOfWeek.Friday).GetString(), Is.EqualTo("Friday"));
        Assert.That(ValueCodec.Deserialize(value, ValueCodec.FromEditorText(value, "Monday")), Is.EqualTo(DayOfWeek.Monday));
        Assert.That(ValueValidation.Validate(value, JsonSerializer.SerializeToElement("monday")), Is.Not.Null);
        Assert.That(ValueValidation.Validate(value, JsonSerializer.SerializeToElement(1)), Is.Not.Null);
        Assert.That(() => ValueCodec.Serialize(value, (DayOfWeek)20), Throws.TypeOf<JsonException>());
    }

    [Test]
    public void JsonDefaultsAreValidatedAndMutableMembersAreIndependent()
    {
        var arguments = JsonSerializer.SerializeToElement(new { numbers = new[] { 1, 2 }, name = (string?)null });
        var first = ValueCodec.DeserializeFields<Options>(Options.Schema, arguments);
        var second = ValueCodec.DeserializeFields<Options>(Options.Schema, arguments);
        first.Numbers.Add(3);
        Assert.That(second.Numbers, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(ValueCodec.ParseDefault(new ValueDescriptor(typeof(int)), "1").GetInt32(), Is.EqualTo(1));
        Assert.That(() => ValueCodec.ParseDefault(new ValueDescriptor(typeof(int)), "\"wrong\""), Throws.ArgumentException);
    }

    [Test]
    public void TypedCompositePreservesNullDefaultsAndCompiledEnvelope()
    {
        var received = new List<Options>();
        var definition = DefaultCompositeEntryBase.ForDto<Options>("test.expand", (context, options) =>
        {
            received.Add(options);
            return [new PrimitiveEntry("test.emit", JsonSerializer.SerializeToElement(new { count = options.Numbers.Count }), "batch")
                { Condition = context.Condition }];
        });
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test",
            [new DefaultPrimitiveEntryBase("test.emit", new DynamicParameterTable([new("count", typeof(int))]))], [definition])]);
        var result = GalgroupCompiler.Compile(new GroupDocument
        {
            Entries = [new() { Id = "source", Type = "test.expand", Condition = "enabled", Parameters = new()
                { ["name"] = JsonSerializer.SerializeToElement<object?>(null) } }]
        }, catalog);
        Assert.That(received[0].Name, Is.Null);
        Assert.That(received[0].Numbers, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(result.Document.Entries.Single().BatchId, Is.EqualTo("batch"));
        Assert.That(result.Document.Entries.Single().Condition, Is.EqualTo("enabled"));
        Assert.That(result.SourceMap.ContainsKey("source"), Is.True);
        Assert.That(() => GalgroupCompiler.Compile(new GroupDocument
        {
            Entries = [new() { Id = "invalid", Type = "test.expand", Parameters = new() { ["unknown"] = JsonSerializer.SerializeToElement(1) } }]
        }, catalog), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unknown"));
        var authoring = (CompositeEntry)catalog.Create("test.expand", values: new Dictionary<string, string> { ["name"] = "manual" });
        authoring.Compile(new EntryCompileContext { SourceEntryId = "manual", Condition = "" });
        Assert.That(received[1].Name, Is.EqualTo("manual"));
        Assert.That(received[1].Numbers, Is.Not.SameAs(received[0].Numbers));
    }

    private sealed class Options : IFieldDto, IFieldSchema<Options>
    {
        public Options() { }
        public List<int> Numbers { get; set; } = [];
        public string? Name { get; set; }
        public static IReadOnlyList<FieldDescriptor> Schema { get; } =
        [new("numbers", new ValueDescriptor(typeof(List<int>)), defaultValue: ValueCodec.ParseDefault(new ValueDescriptor(typeof(List<int>)), "[1,2]")),
         new("name", new ValueDescriptor(typeof(string), allowNull: true))];
        public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => id == "numbers" ? Numbers : Name;
        public void WriteField(string id, object? value)
        { if (id == "numbers") Numbers = (List<int>)value!; else Name = (string?)value; }
    }
}
