using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Presentation;

public sealed class InvocationFieldDecodingTests
{
    [Test]
    public void TypedDispatchDecodesEachSuppliedFieldOnceAndKeepsInvocationsIndependent()
    {
        var received = new List<Options>();
        var validations = 0;
        using var registration = new GameRegistration().RegisterSchema(new DefaultEntryModule("test",
            [DefaultPrimitiveEntryBase.ForDto<Options>("test.run", validateArguments: _ => validations++)]));
        registration.RegisterImplementation(new PrimitiveModule(registry => registry.Immediate<Options>("test.run",
            (context, options) =>
            {
                Assert.That(context.Arguments.GetProperty("numbers")[0].GetInt32(), Is.EqualTo(1));
                received.Add(options);
            })));
        using var view = registration.BuildView();
        var entry = new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { numbers = new[] { 1, 2 } }));
        NumbersConverter.Reads = 0;
        view.Dispatch(entry, new GameRuntime(null), default);
        received[0].Numbers.Add(3);
        view.Dispatch(entry, new GameRuntime(null), default);
        Assert.That(NumbersConverter.Reads, Is.EqualTo(2));
        Assert.That(validations, Is.EqualTo(2));
        Assert.That(received[1].Numbers, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(received[0], Is.Not.SameAs(received[1]));
        Assert.That(entry.Arguments.GetProperty("numbers").GetArrayLength(), Is.EqualTo(2));
        Assert.That(() => view.Dispatch(new PrimitiveEntry("test.run", JsonSerializer.SerializeToElement(new { unknown = 1 })),
            new GameRuntime(null), default), Throws.TypeOf<InvalidDataException>());
    }

    private sealed class Options : IFieldDto, IFieldSchema<Options>
    {
        public Options() { }
        public List<int> Numbers { get; set; } = [];
        public static IReadOnlyList<FieldDescriptor> Schema { get; } =
            [new("numbers", new ValueDescriptor(typeof(List<int>), jsonOptions: new JsonSerializerOptions
                { Converters = { new NumbersConverter() } }), isRequired: true)];
        public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => Numbers;
        public void WriteField(string id, object? value) => Numbers = (List<int>)value!;
    }

    private sealed class NumbersConverter : JsonConverter<List<int>>
    {
        public static int Reads;
        public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Reads++;
            return JsonSerializer.Deserialize<int[]>(ref reader)!.ToList();
        }
        public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToArray());
    }
}
