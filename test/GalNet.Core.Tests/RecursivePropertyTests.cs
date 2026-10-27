using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;

namespace GeneralTest;

public sealed class RecursivePropertyTests
{
    [Test]
    public void OpenObjectsRetainAtomicJsonExtensionsWithoutLosingKnownValidationOrDuplicateChecks()
    {
        var known = new FieldDescriptor("amount", new(typeof(float)), isRequired: true);
        var extension = new FieldDescriptor("extension", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Scalar()));
        var descriptor = new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.OpenObject([known], extension));
        var source = Json("""{"amount":0.5,"angle":0,"":{"caption":"literal"},"future":[1,{"speaker":"not-a-reference"}]}""");
        using var document = new ValueDocument(new("root", descriptor), source);
        Assert.That(document.Validate(requireAllFields: true), Is.Empty);
        Assert.That(document.GetIterator().Count(), Is.EqualTo(5), "Additional JSON is an explicit atomic value, not an unknown DTO subtree.");
        Assert.That(document.FindProperty([""])!.Value!.Value.GetProperty("caption").GetString(), Is.EqualTo("literal"));
        Assert.That(document.FindProperty(["extension"]), Is.Null, "The additional-value descriptor ID is not an alias for all extension keys.");
        var patch = document.Patch(["amount"], Json("0.8"));
        Assert.That(patch.Issue, Is.Null);
        Assert.That(patch.Value!.Value.GetProperty("future").GetRawText(), Is.EqualTo(source.GetProperty("future").GetRawText()));
        Assert.That(document.Root.Value, Is.EqualTo(source));
        using var duplicate = new ValueDocument(new("root", descriptor), Json("""{"angle":0,"angle":1}"""));
        Assert.That(duplicate.Validate().Single().Code, Is.EqualTo("field.duplicate"));
        using var invalid = new ValueDocument(new("root", descriptor), Json("""{"amount":"bad","angle":0}"""));
        Assert.That(invalid.Validate().Single().Code, Is.EqualTo("value.type"));
        using var closed = new ValueDocument(new("root", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Object([known]))), source);
        Assert.That(closed.GetIterator().Count(node => node.Issue?.Code == "field.unknown"), Is.EqualTo(3));
    }

    [Test]
    public void ExplicitMapValuesInheritTheParentsWireCodec()
    {
        var options = new JsonSerializerOptions { Converters = { new StringBooleanValueConverter() } };
        var value = new ValueDescriptor(typeof(JsonElement), jsonOptions: options).WithStructure(ValueStructure.Map(new("value", new(typeof(bool)))));
        using var document = new ValueDocument(new("root", value), Json("""{"flag":"true"}"""));
        Assert.That(document.FindProperty(["flag"])!.Field.Value.IsStringEncoded, Is.True);
        Assert.That(document.Validate(), Is.Empty);
        var patch = document.Patch(["flag"], Json("\"false\""));
        Assert.That(patch.Issue, Is.Null);
        Assert.That(patch.Value!.Value.GetProperty("flag").GetString(), Is.EqualTo("false"));
    }

    [Test]
    public void PartialChecksDoNotRequireMissingSiblingsAndFullChecksDoNotMaterializeOptionalObjects()
    {
        var required = new FieldDescriptor("required", new(typeof(int)), isRequired: true);
        var optional = new FieldDescriptor("optional", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Object([required])));
        var descriptor = new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Object([required, optional]));
        using var partial = new ValueDocument(new("root", descriptor), Json("{}"));
        Assert.That(partial.Validate(), Is.Empty);
        Assert.That(partial.Validate(requireAllFields: true).Select(issue => issue.Path), Is.EqualTo(new[] { "/required" }));
        Assert.That(partial.Root.Content!.Value.GetRawText(), Is.EqualTo("{}"));
    }

    [Test]
    public void PatchesRetainUnknownSiblingsEncodingAndPresenceAndRejectAmbiguousOrMissingTargets()
    {
        var descriptor = new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.ForType(typeof(RecursiveDto), allowsJsonString: true, ignoresCase: true));
        var source = JsonSerializer.SerializeToElement("""{"cHiLd":{"caption/~":"old","extension":{"x":1}},"other":9}""");
        using var doc = new ValueDocument(new("root", descriptor), source);
        var patch = doc.Patch(["child", "caption/~"], Json("\"new\""));
        Assert.That(patch.Issue, Is.Null);
        var result = Json(patch.Value!.Value.GetString()!);
        Assert.That(result.GetProperty("cHiLd").GetProperty("extension").GetProperty("x").GetInt32(), Is.EqualTo(1));
        Assert.That(result.GetProperty("cHiLd").GetProperty("caption/~").GetString(), Is.EqualTo("new"));
        Assert.That(result.GetProperty("other").GetInt32(), Is.EqualTo(9));
        Assert.That(doc.Root.Value, Is.EqualTo(source));
        using var removed = new ValueDocument(new("root", descriptor), patch.Value);
        Assert.That(Json(removed.Patch(["child", "caption/~"], null).Value!.Value.GetString()!).GetProperty("cHiLd").TryGetProperty("caption/~", out _), Is.False);
        using var list = new ValueDocument(Field(typeof(int[])), Json("[1,2]"));
        Assert.That(list.Patch(["2"], Json("3")).Issue, Is.Not.Null);
        Assert.That(list.Patch(["0"], null).Issue!.Code, Is.EqualTo("structure.element"));
        using var nullObject = new ValueDocument(new("root", descriptor), Json("null"));
        Assert.That(nullObject.Patch(["child"], Json("{}")).Issue, Is.Not.Null);
        using var missing = new ValueDocument(new("root", descriptor), null);
        Assert.That(missing.Patch(["child", "caption/~"], Json("\"value\"")).Issue, Is.Null);
        using var conflict = new ValueDocument(new("root", descriptor), Json("""{"Child":{},"child":{}}"""));
        Assert.That(conflict.Patch(["child"], Json("{}")).Issue, Is.Not.Null);
        using var invalid = new ValueDocument(new("root", descriptor), Json("""{"caption/~":42}"""));
        Assert.That(invalid.FindProperty(["caption/~"])!.Issue, Is.Not.Null);
        Assert.That(invalid.Patch(["caption/~"], Json("\"fixed\"")).Issue, Is.Null, "Invalid old values must remain repairable through their known field metadata.");
    }

    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static FieldDescriptor Field(Type type) => new("root", new(type));

    [Test]
    public void TraversalUsesWireNamesOptionsAndSemanticElementsWithoutTouchingGetters()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        using var doc = new ValueDocument(new("root", new(typeof(RecursiveDto), jsonOptions: options)), Json("""{"caption/~":"key","child":{"caption/~":"nested"},"extra":{"x":1}}"""));
        var nodes = doc.GetIterator().ToArray();
        Assert.That(doc.FindProperty(["child"])!.Structure, Is.SameAs(doc.Root.Structure), "Recursive types reuse metadata instead of expanding another schema tree.");
        Assert.That(nodes.Where(node => node.Field.Semantic == FieldValueSemantic.LocalizedText).Select(node => node.Path.ToJsonPointer()),
            Is.EquivalentTo(new[] { "/caption~1~0", "/child/caption~1~0" }));
        Assert.That(nodes.Single(node => node.Path.ToJsonPointer() == "/extra").Issue!.Code, Is.EqualTo("field.unknown"));
        Assert.That(doc.FindProperty(["child", "caption/~"])!.Value!.Value.GetString(), Is.EqualTo("nested"));
        Assert.That(RecursiveDto.GetterCalls, Is.Zero);
    }

    [Test]
    public void MapKeysAndListPositionsAreDistinctAndSemanticValuesArePreserved()
    {
        var item = new FieldDescriptor("value", new(typeof(string)), semantic: FieldValueSemantic.CharacterReference);
        using var map = new ValueDocument(new("root", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Map(item))),
            Json("""{"0":"hero","":"empty","a/b~c":"other","value":"literal"}"""));
        Assert.That(map.Root.EnumerateChildren().All(node => node.Path[^1].Kind == PropertySegmentKind.MapKey), Is.True);
        Assert.That(map.FindProperty(["value"])!.Value!.Value.GetString(), Is.EqualTo("literal"));
        Assert.That(map.FindProperty(["a/b~c"])!.Path.ToJsonPointer(), Is.EqualTo("/a~1b~0c"));
        using var list = new ValueDocument(new("root", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Sequence(item))), Json("""["hero","other"]"""));
        Assert.That(list.FindProperty(["0"])!.Path[0].Kind, Is.EqualTo(PropertySegmentKind.Index));
        Assert.That(list.GetIterator().Count(node => node.Field.Semantic == FieldValueSemantic.CharacterReference), Is.EqualTo(2));
    }

    [Test]
    public void RecursiveMetadataIsLazyAndTraversalHasDepthNodeAndCancellationBounds()
    {
        using var doc = new ValueDocument(Field(typeof(RecursiveDto)), Json("""{"Child":{"Child":{"Child":{"caption/~":"deep"}}}}"""));
        Assert.That(doc.GetIterator(new(MaximumDepth: 2)).Last().Issue!.Code, Is.EqualTo("analysis.limit"));
        using var wide = new ValueDocument(Field(typeof(int[])), Json("[1,2,3,4,5]"));
        var nodes = wide.GetIterator(new(MaximumNodes: 3)).ToArray();
        Assert.That(nodes.Last().Issue!.Code, Is.EqualTo("analysis.limit"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => wide.GetIterator(token: cancel.Token).ToArray());
    }

    [Test]
    public void MissingNullUnknownDuplicateAndStringEncodedObjectsRetainTheirIdentity()
    {
        var value = new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.ForType(typeof(RecursiveDto), allowsJsonString: true, ignoresCase: true));
        using var encoded = new ValueDocument(new("root", value), JsonSerializer.SerializeToElement("""{"Child":{"caption/~":"nested"}}"""));
        var child = encoded.FindProperty(["child", "caption/~"])!;
        Assert.That(child.Path.ToJsonPointer(), Is.Empty);
        Assert.That(child.Path.ToLogicalPointer(), Is.EqualTo("/Child/caption~1~0"));
        var rewritten = child.Path.Replace(encoded.Root.Value!.Value, Json("\"changed\""));
        Assert.That(Json(rewritten.GetString()!).GetProperty("Child").GetProperty("caption/~").GetString(), Is.EqualTo("changed"));
        using var duplicate = new ValueDocument(new("root", value), Json("""{"Child":{},"child":{}}"""));
        Assert.That(duplicate.GetIterator().Any(node => node.Issue?.Code == "field.duplicate"), Is.True);
        Assert.That(duplicate.FindProperty(["child"]), Is.Null);
        using var missing = new ValueDocument(Field(typeof(RecursiveDto)), null);
        Assert.That(missing.Root.IsProvided, Is.False);
        Assert.That(missing.Root.EnumerateChildren(true).All(node => !node.IsProvided), Is.True);
        using var nil = new ValueDocument(Field(typeof(RecursiveDto)), Json("null"));
        Assert.That(nil.Root.IsProvided, Is.True); Assert.That(nil.Root.EnumerateChildren(), Is.Empty);
        using var malformed = new ValueDocument(new("root", value), JsonSerializer.SerializeToElement("{bad"));
        Assert.That(malformed.Root.Issue!.Code, Is.EqualTo("field.objectRequired"));
    }

    [Test]
    public void SameTypeDifferentOptionsAndDynamicContextsCannotShareAnIncorrectShape()
    {
        using var resolver = new ValueStructureResolver();
        using var pascal = new ValueDocument(Field(typeof(RecursiveDto)), Json("""{"Child":{}}"""), resolver);
        using var camel = new ValueDocument(new("root", new(typeof(RecursiveDto), jsonOptions: new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })), Json("""{"child":{}}"""), resolver);
        Assert.That(pascal.Root.Structure.Members.Single(member => member.Field.Id == "child").WireName, Is.EqualTo("Child"));
        Assert.That(camel.Root.Structure.Members.Single(member => member.Field.Id == "child").WireName, Is.EqualTo("child"));
        var dynamicField = new FieldDescriptor("root", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.Dynamic("test")));
        using var known = new ValueDocument(dynamicField, Json("{}"), resolver, new Provider(), true);
        using var unknown = new ValueDocument(dynamicField, Json("""{"x":1}"""), resolver, new Provider(), false);
        Assert.That(known.Root.Structure.Kind, Is.EqualTo(ValueStructureKind.Object));
        Assert.That(unknown.Root.Issue!.Code, Is.EqualTo("field.opaque"));
        resolver.Dispose();
        using var late = new ValueDocument(Field(typeof(int)), Json("1"), resolver);
        Assert.Throws<ObjectDisposedException>(() => _ = late.Root.Structure);
    }

    [Test]
    public void AnObjectConverterWithoutAnExplicitWireAdapterIsNotTraversedAsDtoMembers()
    {
        var options = new JsonSerializerOptions { Converters = { new OpaqueConverter() } };
        using var doc = new ValueDocument(new("root", new(typeof(RecursiveDto), jsonOptions: options)), Json("""{"hidden":"key"}"""));
        Assert.That(doc.Root.Issue!.Code, Is.EqualTo("field.opaque"));
        Assert.That(doc.GetIterator().Count(), Is.EqualTo(1));
        using var encoded = new ValueDocument(new("root", new(typeof(RecursiveDto), jsonOptions: options)), Json("\"opaque\""));
        Assert.That(encoded.Root.Issue!.Code, Is.EqualTo("field.opaque"), "A codec's scalar wire shape is not proof that its DTO has no references.");
        using var incorrect = new ValueDocument(new("root", new ValueDescriptor(typeof(JsonElement)).WithStructure(ValueStructure.ForType(typeof(int)))), Json("""{"hidden":"key"}"""));
        Assert.That(incorrect.Root.Issue!.Code, Is.EqualTo("schema.invalid"));
    }

    private sealed class Provider : IValueStructureProvider
    {
        public ValueStructure? Resolve(string id, FieldDescriptor field, JsonElement? value, PropertyPath path, object? context) =>
            context is true ? ValueStructure.Object([]) : null;
    }
    private sealed class OpaqueConverter : JsonConverter<RecursiveDto>
    {
        public override RecursiveDto Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, RecursiveDto value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
    public sealed class RecursiveDto : IFieldDto
    {
        public static int GetterCalls;
        [Field(0), JsonPropertyName("caption/~")] public string Caption { get { GetterCalls++; throw new InvalidOperationException(); } set { } }
        [Field(1)] public RecursiveDto? Child { get { GetterCalls++; throw new InvalidOperationException(); } set { } }
        public static IReadOnlyList<FieldDescriptor> Schema { get; } =
        [new("caption/~", new(typeof(string)), semantic: FieldValueSemantic.LocalizedText), new("child", new(typeof(RecursiveDto)))];
        public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => throw new NotSupportedException();
        public void WriteField(string id, object? value) => throw new NotSupportedException();
    }
}
