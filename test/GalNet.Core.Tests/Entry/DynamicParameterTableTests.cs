using System.Text.Json;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GeneralTest.Entry;

public class DynamicParameterTableTests
{
    [Test]
    public void TableIsFrozenAndRetainsRuntimeTypeContracts()
    {
        var table = new DynamicParameterTable(
        [
            new DynamicParameterDescriptor("count", typeof(int), isRequired: true),
            new DynamicParameterDescriptor("enabled", typeof(bool), defaultValue: JsonSerializer.SerializeToElement(true))
        ]);

        Assert.That(table, Is.Not.InstanceOf<IDictionary<string, DynamicParameterDescriptor>>());
        Assert.That(table["count"].ValueType, Is.EqualTo(typeof(int)));
        Assert.That(table["enabled"].DefaultValue!.Value.GetBoolean(), Is.True);
        Assert.That(() => new DynamicParameterTable(
            [new DynamicParameterDescriptor("count", typeof(int)), new DynamicParameterDescriptor("count", typeof(int))]),
            Throws.ArgumentException);
    }

    [Test]
    public void DescriptorRejectsJsonDefaultsThatDoNotMatchItsRuntimeType()
    {
        Assert.That(() => new DynamicParameterDescriptor(
                "count",
                typeof(int),
                defaultValue: JsonSerializer.SerializeToElement("not a number")),
            Throws.ArgumentException);
    }

    [Test]
    public void EditorValuesBecomeTypedJsonWithoutClrTypeMetadata()
    {
        var number = ValueCodec.FromEditorText(new(typeof(int)), "12");
        var boolean = ValueCodec.FromEditorText(new(typeof(bool)), "true");
        var payload = ValueCodec.FromEditorText(new(typeof(JsonElement)), """{ "enabled": true }""");

        Assert.That(number.ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(boolean.ValueKind, Is.EqualTo(JsonValueKind.True));
        Assert.That(payload.GetProperty("enabled").GetBoolean(), Is.True);
        Assert.That(number.GetRawText(), Does.Not.Contain("System.Int32"));
    }
}
