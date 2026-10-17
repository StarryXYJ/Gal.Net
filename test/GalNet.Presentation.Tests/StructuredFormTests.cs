using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Presentation;

public sealed class StructuredFormTests
{
    [Test]
    public async Task NestedDtoUsesWireNamesOverridesAndUnknownJsonWithoutCreatingOrWritingOnOpen()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            StructuredDetails.Constructions = 0;
            var binding = new Binding("{\"caption\":\"old\",\"Level\":4,\"opaque\":{\"keep\":true}}");
            var registry = EditorRegistry.CreateDefault(); var roots = 0; var members = 0;
            registry.RegisterDto<StructuredDetails>(context => { roots++; return new(context.CreateDefaultForm()); });
            registry.RegisterMember<StructuredDetails>(item => item.Level, context => { members++; return DefaultFieldEditors.CreateNumeric(context); });
            var form = Form(new("details", new(typeof(StructuredDetails))), binding, registry);
            var window = Mount(form);
            try
            {
                var editor = (StructuredFieldEditor)form.Editors.Single().Control;
                Assert.That(editor.Content, Is.Null); Assert.That(binding.Commits, Is.Zero); Assert.That(StructuredDetails.Constructions, Is.Zero);
                editor.IsExpanded = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.That(roots, Is.EqualTo(1)); Assert.That(members, Is.EqualTo(1)); Assert.That(StructuredDetails.Constructions, Is.Zero);
                var nested = editor.GetVisualDescendants().OfType<SchemaForm>().Single();
                var caption = nested.Contexts.Single(context => context.Field.Id == "caption");
                ((TextBox)nested.Editors.Single(item => item.Context == caption).Control).Text = "edited";
                Assert.That(await caption.CommitTextAsync(), Is.True);
                Assert.That(binding.Value.Json!.Value.GetProperty("caption").GetString(), Is.EqualTo("edited"));
                Assert.That(binding.Value.Json.Value.GetProperty("Level").GetInt32(), Is.EqualTo(4));
                Assert.That(binding.Value.Json.Value.TryGetProperty("level", out _), Is.False);
                Assert.That(binding.Value.Json.Value.GetProperty("opaque").GetProperty("keep").GetBoolean(), Is.True);
                Assert.That(binding.Commits, Is.EqualTo(1));
                editor.IsExpanded = false; Dispatcher.UIThread.RunJobs();
                Assert.That(await caption.CommitValueAsync("late"), Is.False); Assert.That(editor.Content, Is.Null);
                editor.IsExpanded = true; Dispatcher.UIThread.RunJobs(); Assert.That(binding.Commits, Is.EqualTo(1));
            }
            finally { window.Close(); }
            Assert.That(binding.Subscribers, Is.Zero);
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ListsEditAddRemoveAndMoveWithObsoleteIndexBindingsRejected()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding("[1,2]"); var form = Form(new("items", new(typeof(List<int>))), binding);
            var window = Mount(form);
            try
            {
                var editor = (StructuredFieldEditor)form.Editors.Single().Control; editor.IsExpanded = true; Pump(window);
                var rows = editor.GetVisualDescendants().OfType<SchemaForm>().ToArray(); Assert.That(rows, Has.Length.EqualTo(2));
                var old = rows[0].Contexts.Single(); old.Text = "5"; Assert.That(await old.CommitTextAsync(), Is.True);
                Assert.That(binding.Value.Json!.Value[0].GetInt32(), Is.EqualTo(5));
                Assert.That(rows[1].Contexts.Single().IsEnabled, Is.True);
                Click(editor, "down"); Pump(window); Assert.That(binding.Value.Json.Value.EnumerateArray().Select(item => item.GetInt32()), Is.EqualTo(new[] { 2, 5 }));
                Assert.That(await old.CommitValueAsync(9), Is.False); Assert.That(binding.Commits, Is.EqualTo(2));
                Click(editor, "add"); Pump(window); Assert.That(binding.Value.Json.Value[2].GetInt32(), Is.EqualTo(0));
                Click(editor, "remove"); Pump(window); Assert.That(binding.Value.Json.Value.EnumerateArray().Select(item => item.GetInt32()), Is.EqualTo(new[] { 5, 0 }));
                Assert.That(binding.Commits, Is.EqualTo(4));
            }
            finally { window.Close(); }
            Assert.That(binding.Subscribers, Is.Zero); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ExternalArrayChangeCancelsPendingElementCommitBeforeItCanOverwriteTheNewOrder()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding("[1,2]"); var completion = new TaskCompletionSource();
            binding.Commit = async (value, token) => { await completion.Task.WaitAsync(token); token.ThrowIfCancellationRequested(); binding.Value = value; binding.Notify(); return null; };
            var form = Form(new("items", new(typeof(int[]))), binding); var window = Mount(form);
            try
            {
                var editor = (StructuredFieldEditor)form.Editors.Single().Control; editor.IsExpanded = true; Pump(window);
                var field = editor.GetVisualDescendants().OfType<SchemaForm>().First().Contexts.Single(); field.Text = "9";
                var pending = field.CommitTextAsync();
                binding.Value = new(Json("[7,8]")); binding.Notify(); Pump(window);
                Assert.That(await pending, Is.False); Assert.That(binding.Value.Json!.Value[0].GetInt32(), Is.EqualTo(7));
                Assert.That(await field.CommitValueAsync(99), Is.False);
            }
            finally { completion.TrySetResult(); window.Close(); }
            Assert.That(binding.Subscribers, Is.Zero); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task PresenceActionsKeepOmittedDefaultAndNullDistinctAndRevertOnlyTheInput()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding(); var form = Form(new("optional", new(typeof(int?)), defaultValue: Json("5")), binding); var window = Mount(form);
            try
            {
                Assert.That(((TextBox)form.Editors.Single().Control).Text, Is.EqualTo("5")); Assert.That(binding.Value.IsProvided, Is.False); Assert.That(binding.Commits, Is.Zero);
                var actions = form.GetVisualDescendants().OfType<FieldEditorActions>().Single();
                Click(actions, "UseDefault"); Assert.That(binding.Value.Json!.Value.GetInt32(), Is.EqualTo(5));
                Click(actions, "SetNull"); Assert.That(binding.Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
                Click(actions, "OmitField"); Assert.That(binding.Value.IsProvided, Is.False);
                ((TextBox)form.Editors.Single().Control).Text = "-"; Assert.That(await form.Contexts.Single().CommitTextAsync(), Is.False);
                Click(actions, "RevertDraft"); Assert.That(form.Contexts.Single().HasDraft, Is.False); Assert.That(binding.Commits, Is.EqualTo(3));
                Assert.That(((TextBox)form.Editors.Single().Control).Text, Is.EqualTo("5"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task JsonFallbackIsMultilineAndUnsupportedHintsDoNotRemoveTheOtherFields()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var binding = new Binding("{\"x\":1}"); var form = Form(new("value", new(typeof(Dictionary<string, int>))), binding); var window = Mount(form);
            try
            {
                var input = (TextBox)form.Editors.Single().Control; Assert.That(input.AcceptsReturn, Is.True); Assert.That(form.Editors.Single().IsJsonFallback, Is.True);
                input.Text = "{"; Assert.That(await form.Contexts.Single().CommitTextAsync(), Is.False); Assert.That(binding.Commits, Is.Zero);
                input.Text = "{\"x\":2}"; input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Assert.That(binding.Commits, Is.Zero);
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control }); Assert.That(binding.Value.Json!.Value.GetProperty("x").GetInt32(), Is.EqualTo(2));
                form.Schema = [new("value", new(typeof(Dictionary<string, int>)), editor: new("unavailable.editor"))];
                Assert.That(form.Diagnostics.Single().Code, Is.EqualTo("editor.fallback")); Assert.That(form.Editors, Has.Count.EqualTo(1));
                binding.Value = new(Json("\"before\"")); form.Schema = [new("value", new(typeof(string)), editor: new("unavailable.editor"))]; binding.Notify();
                input = (TextBox)form.Editors.Single().Control; Assert.That(input.Text, Is.EqualTo("\"before\""));
                input.Text = "\"after\""; Assert.That(await form.Contexts.Single().CommitTextAsync(), Is.True);
                Assert.That(binding.Value.Json!.Value.GetString(), Is.EqualTo("after"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task RecursiveAndLongCollectionsHaveBoundedLazyJsonFallbacks()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding("{}"); var form = Form(new("root", new(typeof(StructuredDetails))), binding); var window = Mount(form);
            try
            {
                var current = (StructuredFieldEditor)form.Editors.Single().Control; var levels = 0;
                while (true)
                {
                    current.IsExpanded = true; Pump(window); levels++;
                    var nested = current.GetVisualDescendants().OfType<SchemaForm>().First();
                    var next = nested.Editors.Single(item => item.Context.Field.Id == "child").Control;
                    if (next is not StructuredFieldEditor structure) { Assert.That(next.Tag, Is.EqualTo("schema.json")); break; }
                    current = structure; Assert.That(levels, Is.LessThan(9));
                }
                Assert.That(levels, Is.EqualTo(8)); Assert.That(binding.Commits, Is.Zero);
                form.Schema = [new("items", new(typeof(int[])))]; binding.Value = new(JsonSerializer.SerializeToElement(Enumerable.Range(0, 201).ToArray())); binding.Notify();
                var list = (StructuredFieldEditor)form.Editors.Single().Control; list.IsExpanded = true; Pump(window);
                Assert.That(list.Content, Is.TypeOf<TextBox>()); Assert.That(binding.Commits, Is.Zero);
            }
            finally { window.Close(); }
            Assert.That(binding.Subscribers, Is.Zero);
        }, CancellationToken.None);
    }

    private static SchemaForm Form(FieldDescriptor field, Binding binding, EditorRegistry? registry = null) => new() { Schema = [field], Registry = registry ?? EditorRegistry.CreateDefault(), Binding = _ => binding };
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Click(Control root, string name) => root.GetVisualDescendants().OfType<Button>().First(button => button.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Window Mount(Control control)
    {
        var window = new Window { Content = control, Width = 900, Height = 900 };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/")) { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
        window.Show(); Pump(window); return window;
    }
    private sealed class Binding(string? json = null) : IFieldBinding
    {
        private EventHandler? _changed;
        public FieldValue Value { get; set; } = json is null ? default : new(Json(json));
        public int Subscribers { get; private set; }
        public int Commits { get; private set; }
        public Func<FieldValue, CancellationToken, Task<ValueIssue?>>? Commit { get; set; }
        public FieldValue Read() => Value;
        public event EventHandler? Changed { add { Subscribers++; _changed += value; } remove { Subscribers--; _changed -= value; } }
        public void Notify() => _changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        { Commits++; if (Commit is { } commit) return commit(value, token); Value = value; Notify(); return Task.FromResult<ValueIssue?>(null); }
    }
}

[GenerateFieldSchema]
public partial class StructuredDetails
{
    public static int Constructions { get; set; }
    public StructuredDetails() => Constructions++;
    [Field(0), JsonPropertyName("caption")] public string Title { get; set; } = "";
    [Field(1)] public int Level { get; set; } = 3;
    [Field(2)] public StructuredDetails? Child { get; set; }
}
