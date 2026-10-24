using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Presentation;

public class SchemaFormTests
{
    private static readonly string[] CreationAndReleaseErrors = ["editor.create", "editor.release"];
    [Test]
    public async Task DefaultFactoriesMountInDeclarationOrderAndCreateIndependentControls()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var fields = new[] { Field("z", typeof(long)), Field("text", typeof(string)), Field("bool", typeof(bool)), Field("other", typeof(JsonElement)) };
            var bindings = new Dictionary<string, Binding>
            {
                ["z"] = new("9007199254740993"),
                ["text"] = new("\"hello\""),
                ["bool"] = new("true"),
                ["other"] = new("{\"name\":\"a\"}")
            };
            var form = new SchemaForm { Schema = fields, Registry = EditorRegistry.CreateDefault(), Binding = field => bindings[field.Id] };
            var window = Mount(form);
            Assert.That(form.Diagnostics, Is.Empty);
            Assert.That(form.Editors.Select(item => item.Context.Field.Id), Is.EqualTo(fields.Select(field => field.Id)));
            Assert.That(form.Editors[0].Control, Is.TypeOf<TextBox>());
            Assert.That(((TextBox)form.Editors[0].Control).Text, Is.EqualTo("9007199254740993"));
            Assert.That(form.Editors[2].Control, Is.TypeOf<CheckBox>());
            Assert.That(form.Editors.Select(item => item.Control).Distinct().Count(), Is.EqualTo(4));
            Assert.That(window.GetVisualDescendants(), Does.Contain(form.Editors[0].Control));
            Assert.That(bindings.Values.All(binding => binding.Commits == 0), Is.True);
            window.Close();
            Assert.That(bindings.Values.All(binding => binding.Subscribers == 0), Is.True);
        }, CancellationToken.None);
    }

    [Test]
    public async Task NumericDraftsValidateWithoutWritingAndRetainIntegerPrecisionOnCommit()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding("1");
            var form = Form(Field("number", typeof(long)), binding);
            var window = Mount(form);
            var text = (TextBox)form.Editors[0].Control;
            var context = form.Contexts[0];
            text.Text = "-";
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.That(context.Issue!.Code, Is.EqualTo("value.type"));
            text.Text = "1.";
            Assert.That(await context.CommitTextAsync(), Is.False);
            Assert.That(binding.Commits, Is.Zero);
            text.Text = "9007199254740993";
            text.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.That(binding.Value.Json!.Value.GetInt64(), Is.EqualTo(9007199254740993L));
            Assert.That(binding.Commits, Is.EqualTo(1));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task RequiredMissingNullAndDefaultRemainDistinct()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding();
            var form = Form(new("value", new(typeof(string)), isRequired: true), binding);
            var window = Mount(form);
            ((TextBox)form.Editors[0].Control).RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.That(binding.Commits, Is.Zero);
            Assert.That(await form.Contexts[0].CommitAsync(default), Is.False);
            Assert.That(form.Contexts[0].Issue!.Code, Is.EqualTo("field.required"));
            Assert.That(await form.Contexts[0].CommitAsync(new(Json("null"))), Is.True);
            Assert.That(binding.Value.IsProvided, Is.True);
            Assert.That(binding.Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
            ((TextBox)form.Editors[0].Control).RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.That(binding.Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
            form.Schema = [new("value", new(typeof(string)), defaultValue: Json("\"default\""))];
            Assert.That(await form.Contexts[0].CommitAsync(default), Is.True);
            Assert.That(binding.Value.IsProvided, Is.False);
            Assert.That(form.Contexts[0].Text, Is.EqualTo("default"));
            ((TextBox)form.Editors[0].Control).RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.That(binding.Value.IsProvided, Is.False);
            form.Schema = [new("value", new(typeof(string), allowNull: false))];
            Assert.That(await form.Contexts[0].CommitAsync(new(Json("null"))), Is.False);
            Assert.That(form.Contexts[0].Issue!.Code, Is.EqualTo("value.null"));
            Assert.That(binding.Commits, Is.EqualTo(2));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ChoiceOptionsAndHostStateUpdateWithoutRecreatingControls()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding("\"a\"") { Options = [new("a", "Alpha", Json("\"a\""))] };
            var form = Form(new("choice", new(typeof(string)), editor: new(ValueEditorIds.Choice)), binding);
            var window = Mount(form);
            var item = form.Editors[0];
            var control = (ComboBox)item.Control;
            binding.Options = [new("a", "Translated Alpha", Json("\"a\"")), new("b", "Beta", Json("\"b\""))];
            binding.Notify();
            Assert.That(form.Editors[0], Is.SameAs(item));
            Assert.That(((FieldEditorOption)control.Items[0]!).Label, Is.EqualTo("Translated Alpha"));
            control.SelectedItem = control.Items[1];
            Assert.That(binding.Value.Json!.Value.GetString(), Is.EqualTo("b"));
            binding.IsEnabled = false;
            binding.IsVisible = false;
            binding.Notify();
            Assert.That(control.IsEnabled, Is.False);
            Assert.That(control.IsVisible, Is.False);
            Assert.That(form.Editors[0].Context.IsVisible, Is.False);
            window.Close();
        }, CancellationToken.None);
    }

    [Test]
    public async Task ToggleAndSliderUseSharedValidationAndDoNotWriteOnRefresh()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding("false");
            var form = Form(Field("value", typeof(bool)), binding);
            var window = Mount(form);
            ((CheckBox)form.Editors[0].Control).IsChecked = true;
            Assert.That(binding.Value.Json!.Value.GetBoolean(), Is.True);
            binding.Value = new(Json("0"));
            form.Schema = [new("value", new(typeof(double), constraints: Json("{\"minimum\":0,\"maximum\":10}")), editor: new(ValueEditorIds.Slider, Step: .5))];
            binding.Notify();
            var slider = (Slider)form.Editors[0].Control;
            slider.Value = 2.5;
            Assert.That(binding.Value.Json!.Value.GetDouble(), Is.EqualTo(2.5));
            Assert.That(await form.Contexts[0].CommitAsync(new(Json("11"))), Is.False);
            Assert.That(form.Contexts[0].Issue!.Code, Is.EqualTo("value.range"));
            var calls = binding.Commits;
            binding.Value = new(Json("5"));
            binding.Notify();
            Assert.That(slider.Value, Is.EqualTo(5));
            Assert.That(binding.Commits, Is.EqualTo(calls));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [TestCase("missing", typeof(string))]
    [TestCase(ValueEditorIds.Numeric, typeof(string))]
    [TestCase(ValueEditorIds.Slider, typeof(decimal))]
    public async Task UnsupportedEditorsReportDiagnosticsAndReleaseBindings(string id, Type type)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding();
            var form = Form(new("value", new(type), editor: new(id)), binding);
            var window = Mount(form);
            Assert.That(form.Editors.Single().IsJsonFallback, Is.True);
            Assert.That(form.Diagnostics.Single().Code, Is.EqualTo("editor.fallback"));
            Assert.That(binding.Subscribers, Is.EqualTo(1));
            window.Close();
            Assert.That(binding.Subscribers, Is.Zero);
        }, CancellationToken.None);
    }

    [Test]
    public async Task CustomFactoriesDefaultsAndReplacementAreExplicit()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var registry = new EditorRegistry();
            registry.Register("my.editor", _ => new(new TextBlock { Text = "custom" }));
            Assert.Throws<ArgumentException>(() => registry.Register("my.editor", _ => new(new TextBlock())));
            registry.RegisterDefault(typeof(string), "my.editor");
            Assert.Throws<ArgumentException>(() => registry.RegisterDefault(typeof(string), "my.editor"));
            Assert.Throws<KeyNotFoundException>(() => registry.Replace("unregistered", _ => new(new TextBlock())));
            var binding = new Binding("\"a\"");
            var form = Form(Field("value", typeof(string)), binding);
            form.Registry = registry;
            var window = Mount(form);
            Assert.That(((TextBlock)form.Editors[0].Control).Text, Is.EqualTo("custom"));
            registry.Replace("my.editor", _ => new(new TextBlock { Text = "replacement" }));
            form.RefreshEditors();
            Assert.That(((TextBlock)form.Editors[0].Control).Text, Is.EqualTo("replacement"));
            form.Schema = [new("value", new(typeof(string)), editor: new("my.editor"))];
            Assert.That(form.Diagnostics, Is.Empty);
            window.Close();
        }, CancellationToken.None);
    }

    [Test]
    public async Task SwitchingInputsAndRemountingDisposesEverySubscription()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var old = new Binding("1");
            var next = new Binding("2");
            var form = Form(Field("value", typeof(int)), old);
            var panel = new StackPanel { Children = { form } };
            var window = Mount(panel);
            var oldControl = (TextBox)form.Editors[0].Control;
            oldControl.Focus();
            oldControl.Text = "3";
            form.Binding = _ => next;
            Assert.That(old.Subscribers, Is.Zero);
            Assert.That(old.Commits, Is.Zero, "detaching a dirty focused editor must not commit it");
            oldControl.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
            Assert.That(old.Commits, Is.Zero);
            old.Value = new(Json("8"));
            old.Notify();
            Assert.That(((TextBox)form.Editors[0].Control).Text, Is.EqualTo("2"));
            form.Registry = EditorRegistry.CreateDefault();
            Assert.That(next.Subscribers, Is.EqualTo(1));
            panel.Children.Remove(form);
            Assert.That(next.Subscribers, Is.Zero);
            panel.Children.Add(form);
            Assert.That(next.Subscribers, Is.EqualTo(1));
            window.Close();
            Assert.That(next.Subscribers, Is.Zero);
        }, CancellationToken.None);
    }

    [Test]
    public async Task PartialFactoryFailureContinuesCleanupAndCanRecover()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding();
            var releases = 0;
            var registry = new EditorRegistry();
            registry.Register("good", _ => new(new TextBlock(), () => { releases++; throw new IOException("release failure"); }));
            registry.Register("bad", _ => throw new InvalidOperationException("factory failure"));
            var form = new SchemaForm
            {
                Schema = [new("one", new(typeof(string)), editor: new("good")), new("two", new(typeof(string)), editor: new("bad"))],
                Registry = registry,
                Binding = _ => binding
            };
            var window = Mount(form);
            Assert.That(form.Editors, Is.Empty);
            Assert.That(form.Diagnostics.Select(issue => issue.Code), Is.EquivalentTo(CreationAndReleaseErrors));
            Assert.That(releases, Is.EqualTo(1));
            Assert.That(binding.Subscribers, Is.Zero);
            form.Schema = [Field("value", typeof(string))];
            form.Registry = EditorRegistry.CreateDefault();
            Assert.That(form.Diagnostics, Is.Empty);
            Assert.That(binding.Subscribers, Is.EqualTo(1));
            window.Close();
        }, CancellationToken.None);
    }

    [Test]
    public async Task DuplicateFieldsAndReusedControlInstancesAreRejected()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding();
            var field = Field("same", typeof(string));
            var form = new SchemaForm { Schema = [field, field], Registry = EditorRegistry.CreateDefault(), Binding = _ => binding };
            var window = Mount(form);
            Assert.That(form.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(binding.Subscribers, Is.Zero);
            var control = new TextBlock();
            var releases = 0;
            var registry = new EditorRegistry();
            registry.Register("shared", _ => new(control, () => releases++));
            form.Registry = registry;
            form.Schema = [new("one", new(typeof(string)), editor: new("shared")), new("two", new(typeof(string)), editor: new("shared"))];
            Assert.That(form.Editors, Is.Empty);
            Assert.That(form.Diagnostics.Single().Message, Does.Contain("fresh"));
            Assert.That(binding.Subscribers, Is.Zero);
            Assert.That(releases, Is.EqualTo(2));
            window.Close();
        }, CancellationToken.None);
    }

    [Test]
    public async Task ObsoleteAsyncResultCannotUpdateTheNewBindingOrContext()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var completion = new TaskCompletionSource<ValueIssue?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = new Binding("1") { Commit = (_, _) => completion.Task };
            var next = new Binding("5");
            var form = Form(Field("value", typeof(int)), old);
            var window = Mount(form);
            var context = form.Contexts[0];
            context.Text = "2";
            var pending = context.CommitTextAsync();
            form.Binding = _ => next;
            Assert.That(old.Token.IsCancellationRequested, Is.True);
            var notifications = 0;
            context.PropertyChanged += (_, _) => notifications++;
            completion.SetResult(new("host.failed", "late failure"));
            Assert.That(await pending, Is.False);
            Assert.That(notifications, Is.Zero);
            Assert.That(form.Contexts[0].Text, Is.EqualTo("5"));
            Assert.That(form.Contexts[0].Issue, Is.Null);
            Assert.That(next.Commits, Is.Zero);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task HostFailureAndExternalUpdatesDoNotEraseDrafts()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var completion = new TaskCompletionSource<ValueIssue?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var binding = new Binding("1") { Commit = (_, _) => completion.Task };
            var form = Form(Field("value", typeof(int)), binding);
            var window = Mount(form);
            var context = form.Contexts[0];
            context.Text = "2";
            var pending = context.CommitTextAsync();
            context.Text = "3";
            binding.Value = new(Json("4"));
            binding.Notify();
            Assert.That(context.Text, Is.EqualTo("3"));
            completion.SetResult(new("host.failed", "obsolete error"));
            Assert.That(await pending, Is.False);
            Assert.That(context.Issue, Is.Null);
            Assert.That(context.Text, Is.EqualTo("3"));
            binding.Commit = (_, _) => throw new IOException("save failed");
            Assert.That(await context.CommitTextAsync(), Is.False);
            Assert.That(context.Issue!.Message, Is.EqualTo("save failed"));
            Assert.That(context.Text, Is.EqualTo("3"));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task BackgroundChangesAndInvalidOptionsAreHandledWithoutRebuilding()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding("\"a\"") { Options = [new("a", "A", Json("\"a\""))] };
            var form = Form(new("value", new(typeof(string)), editor: new(ValueEditorIds.Choice)), binding);
            var window = Mount(form);
            var control = form.Editors[0].Control;
            await Task.Run(() => { binding.Value = new(Json("\"b\"")); binding.Notify(); });
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.That(form.Contexts[0].Text, Is.EqualTo("b"));
            binding.Options = [new("same", "A", Json("\"a\"")), new("same", "B", Json("\"b\""))];
            binding.Notify();
            Assert.That(form.Contexts[0].Issue!.Code, Is.EqualTo("field.binding"));
            binding.Options = [new("b", "B", Json("\"b\""))];
            binding.Notify();
            Assert.That(form.Contexts[0].Issue, Is.Null);
            Assert.That(form.Editors[0].Control, Is.SameAs(control));
            Assert.That(binding.Commits, Is.Zero);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EnumAndConstrainedStringDefaultsUseChoiceWithTypedValues()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var binding = new Binding("\"Monday\"");
            var form = Form(new("day", new(typeof(DayOfWeek), jsonOptions: options)), binding);
            var window = Mount(form);
            var choice = (ComboBox)form.Editors[0].Control;
            choice.SelectedItem = form.Contexts[0].Options.Single(option => option.Value.GetString() == "Tuesday");
            Assert.That(binding.Value.Json!.Value.GetString(), Is.EqualTo("Tuesday"));
            form.Schema = [new("choice", new(typeof(string), constraints: Json("{\"enum\":[\"first\",\"second\"]}")))];
            choice = (ComboBox)form.Editors[0].Control;
            choice.SelectedItem = choice.Items[0];
            Assert.That(binding.Value.Json!.Value.GetString(), Is.EqualTo("first"));
            window.Close();
        }, CancellationToken.None);
    }

    [TestCase("{}", typeof(long))]
    [TestCase("{\"minimum\":0,\"maximum\":9007199254740993}", typeof(long))]
    [TestCase("{\"minimum\":0,\"maximum\":2147483648}", typeof(int))]
    public async Task UnsafeOrMissingSliderBoundsAreDiagnosed(string constraints, Type type)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding("0");
            var form = Form(new("value", new(type, constraints: Json(constraints)), editor: new(ValueEditorIds.Slider)), binding);
            var window = Mount(form);
            Assert.That(form.Editors.Single().IsJsonFallback, Is.True);
            Assert.That(form.Diagnostics.Single().Code, Is.EqualTo("editor.fallback"));
            Assert.That(binding.Subscribers, Is.EqualTo(1));
            window.Close();
            Assert.That(binding.Subscribers, Is.Zero);
        }, CancellationToken.None);
    }

    [Test]
    public async Task IntegerSliderDoesNotRoundAnInvalidFractionIntoAValidValue()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding("1");
            var form = Form(new("value", new(typeof(int), constraints: Json("{\"minimum\":0,\"maximum\":10}")), editor: new(ValueEditorIds.Slider)), binding);
            var window = Mount(form);
            ((Slider)form.Editors[0].Control).Value = 2.5;
            Assert.That(binding.Commits, Is.Zero);
            Assert.That(binding.Value.Json!.Value.GetInt32(), Is.EqualTo(1));
            Assert.That(form.Contexts[0].Issue!.Code, Is.EqualTo("value.type"));
            window.Close();
        }, CancellationToken.None);
    }

    [Test]
    public async Task ToggleReadsAndWritesUsingTheProjectsCustomJsonConverter()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var options = new JsonSerializerOptions { Converters = { new YesNoConverter() } };
            var binding = new Binding("\"yes\"");
            var form = Form(new("value", new(typeof(bool), jsonOptions: options)), binding);
            var window = Mount(form);
            var check = (CheckBox)form.Editors[0].Control;
            Assert.That(check.IsChecked, Is.True);
            check.IsChecked = false;
            Assert.That(binding.Value.Json!.Value.GetString(), Is.EqualTo("no"));
            Assert.That(binding.Commits, Is.EqualTo(1));
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class YesNoConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString() == "yes";
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteStringValue(value ? "yes" : "no");
    }

    private static FieldDescriptor Field(string id, Type type) => new(id, new(type));
    private static SchemaForm Form(FieldDescriptor field, Binding binding) => new()
    { Schema = [field], Registry = EditorRegistry.CreateDefault(), Binding = _ => binding };
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static Window Mount(Control control)
    {
        var window = new Window { Content = control, Width = 400, Height = 400 };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/"))
        { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private sealed class Binding : IFieldBinding
    {
        private EventHandler? _changed;
        public Binding(string? text = null) => Value = text is null ? default : new(Json(text));
        public FieldValue Value { get; set; }
        public int Subscribers { get; private set; }
        public int Commits { get; private set; }
        public bool IsEnabled { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public IReadOnlyList<FieldEditorOption>? Options { get; set; }
        public Func<FieldValue, CancellationToken, Task<ValueIssue?>>? Commit { get; set; }
        public CancellationToken Token { get; private set; }
        public event EventHandler? Changed
        {
            add { _changed += value; Subscribers++; }
            remove { _changed -= value; Subscribers--; }
        }
        public FieldValue Read() => Value;
        public void Notify() => _changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        {
            Commits++;
            Token = cancellationToken;
            if (Commit is { } commit) return commit(value, cancellationToken);
            Value = value;
            Notify();
            return Task.FromResult<ValueIssue?>(null);
        }
    }
}
