using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Controls;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Editor;

[NonParallelizable]
public sealed class EntrySchemaFormTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ParticleDtoCanBeAuthoredInEitherOrderWhileFullCompilationStillRejectsIncompleteContent(bool payloadFirst)
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateParticlesModule()]);
        var entry = new EntryEditorItemViewModel { Type = BuiltinEntryIds.PlayParticleEmitter };
        entry.ConfigureParameterFields(catalog, [], []);
        var form = entry.ParameterForm!;
        var id = form.Binding(form.Fields.Single(field => field.Id == "instanceId"));
        var payload = form.Binding(form.Fields.Single(field => field.Id == "parameters"));
        var first = payloadFirst ? payload : id;
        var second = payloadFirst ? id : payload;
        Assert.That(await first.CommitAsync(new(payloadFirst ? Json("{\"particleTexture\":\"snow\"}") : Json("\"snow\"")), default), Is.Null);
        var incomplete = new EditorDocumentRepository(catalog).SerializeEntry(new EditorEntryData { StableId = entry.StableId, Type = entry.Type, Parameters = entry.Source.Parameters });
        Assert.That(() => PrimitiveArgumentHelper.Normalize(catalog.Get(entry.Type).DynamicParameters!, JsonSerializer.SerializeToElement(incomplete.Parameters)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(await second.CommitAsync(new(payloadFirst ? Json("\"snow\"") : Json("{\"particleTexture\":\"snow\"}")), default), Is.Null);
        var saved = new EditorDocumentRepository(catalog).SerializeEntry(new EditorEntryData { StableId = entry.StableId, Type = entry.Type, Parameters = entry.Source.Parameters });
        Assert.That(() => PrimitiveArgumentHelper.Normalize(catalog.Get(entry.Type).DynamicParameters!, JsonSerializer.SerializeToElement(saved.Parameters)), Throws.Nothing);
    }

    [Test]
    public async Task GeneratedDocumentFieldsPreservePresenceAndCanRepairValuesWithoutConstructingDto()
    {
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test", [DefaultPrimitiveEntryBase.ForDto<DocumentOptions>("test.fields", arguments =>
        {
            if (arguments.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Number && count.GetInt32() == 9)
                throw new ArgumentException("Nine is reserved by this project.");
        })])]);
        var entry = new EntryEditorItemViewModel { Type = "test.fields", Parameters = new() { ["optional"] = "null", ["count"] = "true" } };
        DocumentOptions.Constructions = 0;
        entry.ConfigureParameterFields(catalog, [], []);
        var binding = entry.ParameterForm!;
        var count = binding.Binding(binding.Fields[0]);
        var optional = binding.Binding(binding.Fields[1]);
        var required = binding.Binding(binding.Fields[2]);
        Assert.Multiple(() =>
        {
            Assert.That(DocumentOptions.Constructions, Is.Zero);
            Assert.That(entry.ParameterFields, Is.Empty);
            Assert.That(count.Read().Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.True), "Reading must not deserialize an invalid author value into a CLR member.");
            Assert.That(optional.Read().Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(required.Read().IsProvided, Is.False);
        });
        Assert.That(await optional.CommitAsync(new(Json("3")), CancellationToken.None), Is.Null, "Other invalid or missing required fields must not prevent gradual document repair.");
        Assert.That(await count.CommitAsync(new(Json("7")), CancellationToken.None), Is.Null);
        Assert.That(await count.CommitAsync(new(Json("9")), CancellationToken.None), Is.Not.Null);
        Assert.That(entry.Parameters["count"], Is.EqualTo("7"));
        Assert.That(await count.CommitAsync(default, CancellationToken.None), Is.Null);
        Assert.That(count.Read().IsProvided, Is.False, "A schema default is not an authored value.");
        Assert.That(binding.Fields[0].DefaultValue!.Value.GetInt32(), Is.EqualTo(5));
        Assert.That(await required.CommitAsync(default, CancellationToken.None), Is.Not.Null);
        Assert.That(await optional.CommitAsync(default, CancellationToken.None), Is.Null);
        Assert.That(optional.Read().IsProvided, Is.False);
        var saved = new EditorDocumentRepository(catalog).SerializeEntry(new EditorEntryData { StableId = entry.StableId, Type = entry.Type, Parameters = entry.Source.Parameters });
        Assert.That(saved.Parameters, Is.Empty);
        entry.ConfigureParameterFields(catalog, [], []);
        Assert.That(await count.CommitAsync(new(Json("8")), CancellationToken.None), Is.Not.Null);
        Assert.That(entry.Parameters, Is.Empty);
        Assert.That(DocumentOptions.Constructions, Is.Zero);
    }

    [Test]
    public async Task ProjectMetadataPreservesLargeIntegersConvertersAndTypedJsonOnSave()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var booleanOptions = new JsonSerializerOptions { Converters = { new YesNoConverter() } };
            var table = new DynamicParameterTable([
                new("count", typeof(long), constraints: Json("{\"minimum\":0}")),
                new("day", new ValueDescriptor(typeof(DayOfWeek), jsonOptions: options)),
                new("enabled", new ValueDescriptor(typeof(bool), jsonOptions: booleanOptions)), new("title", typeof(string))]);
            var catalog = Catalog(table);
            var entry = new EntryEditorItemViewModel
            {
                Type = "test.fields",
                Parameters = new()
                { ["count"] = "9007199254740993", ["day"] = "\"Monday\"", ["enabled"] = "\"yes\"", ["title"] = "Original" }
            };
            entry.ConfigureParameterFields(catalog, [], []);
            var fields = entry.ParameterFields.Cast<SchemaEntryParameterEditorItemViewModel>().ToArray();
            var form = new SchemaForm { Schema = table.Fields, Registry = EditorFieldEditors.CreateDefault(), Binding = field => fields.Single(item => item.Id == field.Id) };
            var window = Mount(form);
            try
            {
                Assert.That(form.Editors.Select(item => item.Control).OfType<TextBox>().Count(), Is.EqualTo(2));
                Assert.That(form.Editors.All(item => window.GetVisualDescendants().Contains(item.Control)), Is.True);
                Assert.That(form.Contexts[0].Field.Value, Is.SameAs(table["count"].Value));
                var input = (TextBox)form.Editors[0].Control;
                input.Text = "-";
                input.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Assert.That(form.Contexts[0].Issue, Is.Not.Null);
                Assert.That(entry.Parameters["count"], Is.EqualTo("9007199254740993"));
                input.Text = "9007199254740995";
                Assert.That(await form.Contexts[0].CommitTextAsync(), Is.True);
                ((CheckBox)form.Editors[2].Control).IsChecked = false;
                ((ComboBox)form.Editors[1].Control).SelectedItem = form.Contexts[1].Options.Single(item => item.Value.GetString() == "Friday");
                var saved = new EditorDocumentRepository(catalog).SerializeEntry(new EditorEntryData
                { StableId = entry.StableId, Type = entry.Type, Parameters = entry.Source.Parameters });
                Assert.That(saved.Parameters["count"].GetInt64(), Is.EqualTo(9007199254740995L));
                Assert.That(saved.Parameters["day"].GetString(), Is.EqualTo("Friday"));
                Assert.That(saved.Parameters["enabled"].GetString(), Is.EqualTo("no"));
                Assert.That(saved.Parameters["title"].GetString(), Is.EqualTo("Original"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task OmittedDefaultAndExplicitNullableNumberStayDistinctAndOldSchemaCannotCommit()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var table = new DynamicParameterTable([new("count", typeof(int), defaultValue: Json("5")), new("optional", typeof(int?), allowNull: true)]);
            var catalog = Catalog(table);
            var entry = new EntryEditorItemViewModel { Type = "test.fields", Parameters = new() { ["optional"] = "null" } };
            entry.ConfigureParameterFields(catalog, [], []);
            var fields = entry.ParameterFields.Cast<SchemaEntryParameterEditorItemViewModel>().ToArray();
            var form = new SchemaForm { Schema = table.Fields, Registry = EditorFieldEditors.CreateDefault(), Binding = field => fields.Single(item => item.Id == field.Id) };
            var window = Mount(form);
            try
            {
                Assert.That(form.Contexts[0].Value.IsProvided, Is.False);
                Assert.That(((TextBox)form.Editors[0].Control).Text, Is.EqualTo("5"));
                Assert.That(form.Contexts[1].Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(entry.Parameters.ContainsKey("count"), Is.False);
                form.Contexts[0].Text = "7";
                Assert.That(await form.Contexts[0].CommitTextAsync(), Is.True);
                Assert.That(await fields[0].CommitAsync(default, CancellationToken.None), Is.Null);
                Assert.That(entry.Parameters.ContainsKey("count"), Is.False);
                var old = fields[0];
                entry.ConfigureParameterFields(catalog, [], []);
                Assert.That(await old.CommitAsync(new(Json("8")), CancellationToken.None), Is.Not.Null);
                Assert.That(entry.Parameters.ContainsKey("count"), Is.False);
                var oldContext = form.Contexts[0];
                window.Content = null;
                Assert.That(await oldContext.CommitTextAsync(), Is.False);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static IEntryCatalog Catalog(DynamicParameterTable table) => new TargetProfileEntryCatalog(
        [new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.fields", table)])]);
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static Window Mount(SchemaForm form)
    {
        var window = new Window { Width = 500, Height = 400, Content = form };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/"))
        { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
        window.Show();
        window.UpdateLayout();
        Assert.That(form.Diagnostics, Is.Empty);
        return window;
    }

    private sealed class YesNoConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString() == "yes";
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteStringValue(value ? "yes" : "no");
    }
}

[GenerateFieldSchema]
public partial class DocumentOptions
{
    public static int Constructions { get; set; }
    public DocumentOptions() => Constructions++;
    [Field(0)] public int Count { get; set; } = 5;
    [Field(1, AllowNull = true)] public int? Optional { get; set; }
    [Field(2, Required = true, AllowNull = false)] public string? Name { get; set; }
}

public static class EditorFormTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
