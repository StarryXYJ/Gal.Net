using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GeneralTest.Entry;

public sealed class ValueDescriptorTests
{
    [Test]
    public void TextChoicesAreNormalizedToOneTypedEnumConstraint()
    {
        var numeric = new ValueDescriptor(typeof(int), choices: ["1", "2"]);
        Assert.That(numeric.TryGetConstraint("enum", out var choices), Is.True);
        Assert.That(choices[0].GetInt32(), Is.EqualTo(1));
        Assert.That(numeric.TryGetConstraint("options", out _), Is.False);
        Assert.That(ValueValidation.Validate(numeric, Json("3"))!.Code, Is.EqualTo("value.option"));
    }

    private static readonly string[] ExpectedNames = ["first", "second"];
    private static readonly int[] ExpectedNumbers = [1, 2];
    [Test]
    public void NestedObjectsAndArraysRoundTripAndReportInvalidMemberPaths()
    {
        var descriptor = new ValueDescriptor(typeof(Payload), allowNull: false);
        var json = ValueCodec.Serialize(descriptor, new Payload { Count = 2, Names = ["first", "second"] });
        var restored = (Payload)ValueCodec.Deserialize(descriptor, json)!;
        Assert.That(restored.Names, Is.EqualTo(ExpectedNames));
        Assert.That(restored.Count, Is.EqualTo(2));
        var issue = ValueValidation.Validate(descriptor, Json("""{"count":"wrong","Names":[]}"""));
        Assert.That(issue!.Code, Is.EqualTo("value.type"));
        Assert.That(issue.Path, Is.EqualTo("$.count"));
        Assert.That(ValueValidation.Validate(descriptor, Json("""{"Names":[]}"""))!.Code, Is.EqualTo("value.type"));
        Assert.That(json.GetRawText(), Does.Not.Contain("$type"));
    }

    [Test]
    public void CustomJsonOptionsAndEnumRestrictionsAreUsedByBothCodecAndDescription()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter<Mode>());
        var descriptor = new ValueDescriptor(typeof(Mode), constraints: Json("""{"enum":["One"]}"""), jsonOptions: options);
        options.Converters.Clear(); // The descriptor owns a frozen snapshot.
        Assert.That(ValueCodec.Serialize(descriptor, Mode.One).GetString(), Is.EqualTo("One"));
        Assert.That(ValueCodec.Deserialize(descriptor, Json("\"One\"")), Is.EqualTo(Mode.One));
        Assert.That(ValueValidation.Validate(descriptor, Json("\"Two\""))!.Code, Is.EqualTo("value.option"));
        Assert.That(ValueCodec.Describe(descriptor).GetProperty("enum")[0].GetString(), Is.EqualTo("One"));
        Assert.That(() => descriptor.JsonOptions.Converters.Clear(), Throws.InvalidOperationException);
    }

    [Test]
    public void MissingExplicitNullAndRequiredAreIndependent()
    {
        var parameters = new DynamicParameterTable([
            new("optional", typeof(string), allowNull: false),
            new("nullable", typeof(string), isRequired: true, allowNull: true),
            new("count", typeof(int), defaultValue: Json("3"))]);
        var normalized = PrimitiveArgumentHelper.Normalize(parameters, Json("""{"nullable":null}"""));
        Assert.That(normalized.TryGetProperty("optional", out _), Is.False);
        Assert.That(normalized.GetProperty("nullable").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(normalized.GetProperty("count").GetInt32(), Is.EqualTo(3));
        Assert.That(() => PrimitiveArgumentHelper.Normalize(parameters, Json("{}")), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => PrimitiveArgumentHelper.Normalize(parameters, Json("""{"nullable":null,"optional":null}""")), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("2", null)]
    [TestCase("-1", "value.range")]
    [TestCase("6", "value.range")]
    [TestCase("2.5", "value.type")]
    public void IntegerRulesExecuteFromTheSameDescription(string json, string? error)
    {
        var descriptor = new ValueDescriptor(typeof(int), constraints: Json("""{"minimum":0,"maximum":5}"""));
        Assert.That(ValueValidation.Validate(descriptor, Json(json))?.Code, Is.EqualTo(error));
        Assert.That(ValueCodec.Describe(descriptor).GetProperty("minimum").GetInt32(), Is.Zero);
    }

    [Test]
    public void NumericBoundsDoNotRoundLargeIntegersToDouble()
    {
        var descriptor = new ValueDescriptor(typeof(long), constraints: Json("""{"maximum":9007199254740992}"""));
        Assert.That(ValueValidation.Validate(descriptor, Json("9007199254740993"))!.Code, Is.EqualTo("value.range"));
    }

    [Test]
    public void TextArrayBooleanAndFloatingValuesUseSharedConversion()
    {
        var text = new ValueDescriptor(typeof(string), allowNull: false, constraints: Json("""{"minLength":1,"maxLength":3}"""));
        Assert.That(ValueCodec.FromEditorText(text, "hi").GetString(), Is.EqualTo("hi"));
        Assert.That(ValueValidation.Validate(text, Json("\"long\""))!.Code, Is.EqualTo("value.length"));
        var array = new ValueDescriptor(typeof(int[]), constraints: Json("""{"minItems":1,"maxItems":2}"""));
        Assert.That(ValueCodec.Deserialize(array, ValueCodec.FromEditorText(array, "[1,2]")), Is.EqualTo(ExpectedNumbers));
        Assert.That(ValueValidation.Validate(array, Json("[]"))!.Code, Is.EqualTo("value.items"));
        Assert.That(ValueCodec.FromEditorText(new ValueDescriptor(typeof(bool)), "true").GetBoolean(), Is.True);
        Assert.That(ValueCodec.FromEditorText(new ValueDescriptor(typeof(double)), "1.25").GetDouble(), Is.EqualTo(1.25));
        Assert.That(ValueCodec.Deserialize(new ValueDescriptor(typeof(JsonElement)), Json("null")), Is.TypeOf<JsonElement>());
    }

    [Test]
    public void LegacyOptionsDescribeTypedValuesAndNullBypassesNonNullConstraints()
    {
        var descriptor = new ValueDescriptor(typeof(int), allowNull: true, constraints: Json("""{"enum":[1,2]}"""));
        Assert.That(ValueValidation.Validate(descriptor, Json("null")), Is.Null);
        Assert.That(ValueValidation.Validate(descriptor, Json("3"))!.Code, Is.EqualTo("value.option"));
        var description = ValueCodec.Describe(descriptor);
        Assert.That(description.GetProperty("anyOf")[0].GetProperty("enum")[0].GetInt32(), Is.EqualTo(1));
        Assert.That(description.GetProperty("anyOf")[1].GetProperty("type").GetString(), Is.EqualTo("null"));
        var array = new ValueDescriptor(typeof(int[]), constraints: Json("""{"enum":[[1,2]]}"""));
        Assert.That(ValueValidation.Validate(array, Json("[1, 2]")), Is.Null);
    }

    [Test]
    public void JsonSchemaLengthCountsUnicodeCharactersRatherThanUtf16Units()
    {
        var descriptor = new ValueDescriptor(typeof(string), constraints: Json("""{"maxLength":1}"""));
        Assert.That(ValueValidation.Validate(descriptor, JsonSerializer.SerializeToElement("😀")), Is.Null);
        Assert.That(ValueValidation.Validate(descriptor, JsonSerializer.SerializeToElement("😀a"))!.Code, Is.EqualTo("value.length"));
    }

    [Test]
    public void NamedFloatingValuesAreRejectedEvenWhenTheSerializerAcceptsThem()
    {
        var descriptor = new ValueDescriptor(typeof(double), jsonOptions: new JsonSerializerOptions
        { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Assert.That(ValueValidation.Validate(descriptor, Json("\"NaN\""))!.Code, Is.EqualTo("value.finite"));
        Assert.That(() => ValueCodec.Serialize(descriptor, double.PositiveInfinity), Throws.ArgumentException);
    }

    [Test]
    public void InvalidDefaultsAndConstraintDeclarationsAreRejected()
    {
        Assert.That(() => new DynamicParameterDescriptor("count", typeof(int), defaultValue: Json("-1"), constraints: Json("""{"minimum":0}""")), Throws.ArgumentException);
        Assert.That(() => new ValueDescriptor(typeof(string), constraints: Json("""{"maxLength":-1}""")), Throws.ArgumentException);
        Assert.That(() => new ValueDescriptor(typeof(int), constraints: Json("""{"enum":["wrong"]}""")), Throws.ArgumentException);
        Assert.That(() => new ValueDescriptor(typeof(string), constraints: Json("""{"maxLength":"wrong"}""")), Throws.ArgumentException);
        Assert.That(() => new ValueDescriptor(typeof(int), constraints: Json("""{"minimum":3,"maximum":2}""")), Throws.ArgumentException);
    }

    [Test]
    public void EntryCanComposeTheSameDescriptorWithItsCustomEnumCodec()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter<Mode>());
        var value = new ValueDescriptor(typeof(Mode), jsonOptions: options);
        var parameter = new DynamicParameterDescriptor("mode", value, isRequired: true);
        var arguments = PrimitiveArgumentHelper.Normalize(new DynamicParameterTable([parameter]), Json("""{"mode":"One"}"""));
        Assert.That(parameter.Value, Is.SameAs(value));
        Assert.That(ValueCodec.Deserialize(value, arguments.GetProperty("mode")), Is.EqualTo(Mode.One));
    }

    public enum Mode { One, Two }
    public sealed class RecursivePayload { public RecursivePayload? Child { get; init; } }

    [Test]
    public void RecursiveSchemaReferencesResolveWhenStandaloneAndEmbedded()
    {
        var descriptor = new ValueDescriptor(typeof(RecursivePayload), allowNull: false);
        var standalone = ValueCodec.Describe(descriptor);
        Assert.That(CheckReferences(standalone, standalone), Is.GreaterThan(0));
        var embedded = ValueCodec.Describe(descriptor, "#/properties/payload");
        var document = JsonSerializer.SerializeToElement(new { properties = new { payload = embedded } });
        Assert.That(CheckReferences(document, embedded), Is.GreaterThan(0));
        Assert.That(ValueValidation.Validate(descriptor, Json("null"))!.Code, Is.EqualTo("value.null"));
        Assert.That(ValueValidation.Validate(descriptor, Json("""{"Child":{"Child":null}}""")), Is.Null);
        Assert.That(standalone.GetProperty("not").GetProperty("type").GetString(), Is.EqualTo("null"));
    }

    private static int CheckReferences(JsonElement root, JsonElement node)
    {
        var count = 0;
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
                if (property.Name == "$ref")
                {
                    var target = root;
                    foreach (var part in property.Value.GetString()![2..].Split('/'))
                        target = target.ValueKind == JsonValueKind.Array ? target[int.Parse(part, System.Globalization.CultureInfo.InvariantCulture)]
                            : target.GetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
                    Assert.That(target.ValueKind, Is.EqualTo(JsonValueKind.Object));
                    count++;
                }
                else count += CheckReferences(root, property.Value);
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) count += CheckReferences(root, child);
        return count;
    }
    public sealed class Payload
    {
        [JsonPropertyName("count")]
        public required int Count { get; init; }
        public string[] Names { get; init; } = [];
    }
    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
