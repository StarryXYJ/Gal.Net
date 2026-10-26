using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GeneralTest.Entry;

public class FieldDescriptorTests
{
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
