using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GeneralTest.Entry;

public class FieldDescriptorTests
{
    [TestCase(".3", "0.3")]
    [TestCase(".22", "0.22")]
    [TestCase("-.22", "-0.22")]
    [TestCase(" .3e-2 ", "0.003")]
    public void FractionalEditorShorthandIsTypedAndDoesNotRelaxJson(string input, string expected)
    {
        foreach (var type in new[] { typeof(float), typeof(double), typeof(decimal), typeof(float?) })
            Assert.That(ValueCodec.FromEditorText(new(type), input).GetDecimal(), Is.EqualTo(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(ValueCodec.FromEditorText(new(typeof(string)), input).GetString(), Is.EqualTo(input));
        Assert.That(() => ValueCodec.ParseEditorText(new(typeof(int)), input), Throws.InstanceOf<JsonException>());
        Assert.That(() => JsonDocument.Parse(input), Throws.InstanceOf<JsonException>());
    }

    [TestCase("-", true)]
    [TestCase(".", true)]
    [TestCase("-.", true)]
    [TestCase("1.", true)]
    [TestCase("-1.", true)]
    [TestCase("1e-", true)]
    [TestCase(".3e+", true)]
    [TestCase("hello", false)]
    [TestCase(".3", false)]
    [TestCase("1e--", false)]
    public void NumericPrefixesAreOnlyTransientInput(string input, bool incomplete)
    {
        Assert.That(ValueCodec.IsIncompleteEditorNumber(new(typeof(float)), input), Is.EqualTo(incomplete));
        Assert.That(ValueCodec.IsIncompleteEditorNumber(new(typeof(string)), input), Is.False);
        if (incomplete) Assert.That(() => ValueCodec.FromEditorText(new(typeof(float)), input), Throws.InstanceOf<JsonException>());
    }

    private static readonly string[] ExpectedFieldIds = ["z", "a"];
    [Test]
    public void EntryFieldsRetainInputOrderAndCannotBeEditedAfterConstruction()
    {
        var input = new List<DynamicParameterDescriptor> { new("z", typeof(int)), new("a", typeof(string)) };
        var table = new DynamicParameterTable(input);
        input.Clear();
        Assert.That(table.Fields.Select(field => field.Id), Is.EqualTo(ExpectedFieldIds));
        Assert.That(table.Fields[0], Is.SameAs(table["z"].Field));
        Assert.Throws<NotSupportedException>(() => ((IList<FieldDescriptor>)table.Fields).Clear());
    }

    [Test]
    public void DefaultIsValidatedAndClonedWhileRequiredAndNullStayIndependent()
    {
        using var document = JsonDocument.Parse("null");
        var field = new FieldDescriptor("nullable", new(typeof(string)), isRequired: true,
            defaultValue: document.RootElement, editor: new("custom.color"));
        document.Dispose();
        Assert.That(field.DefaultValue!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(field.IsRequired, Is.True);
        Assert.That(field.Editor!.EditorId, Is.EqualTo("custom.color"));
        Assert.Throws<ArgumentException>(() => new FieldDescriptor("int", new(typeof(int)), defaultValue: field.DefaultValue));
    }

    [Test]
    public void EditorTextKeepsJsonStringQuotesForStringEncodedEnumsAndUsesCustomOptions()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var descriptor = new ValueDescriptor(typeof(DayOfWeek), jsonOptions: options);
        var json = ValueCodec.Serialize(descriptor, DayOfWeek.Monday);
        var text = ValueCodec.ToEditorText(descriptor, json);
        Assert.That(text, Is.EqualTo("\"Monday\""));
        Assert.That(ValueCodec.Deserialize(descriptor, ValueCodec.FromEditorText(descriptor, text)), Is.EqualTo(DayOfWeek.Monday));
        Assert.That(ValueCodec.ToEditorText(new(typeof(string)), JsonSerializer.SerializeToElement("Monday")), Is.EqualTo("Monday"));
    }

    [Test]
    public void ClrValidationRejectsNonFiniteAndOutOfRangeValuesWithoutThrowing()
    {
        var descriptor = new ValueDescriptor(typeof(double), constraints: JsonSerializer.SerializeToElement(new { minimum = 0, maximum = 10 }));
        Assert.That(ValueValidation.ValidateValue(descriptor, double.NaN)!.Code, Is.EqualTo("value.finite"));
        Assert.That(ValueValidation.ValidateValue(descriptor, 11d)!.Code, Is.EqualTo("value.range"));
        Assert.That(ValueValidation.ValidateValue(descriptor, .13), Is.Null);
    }
}
