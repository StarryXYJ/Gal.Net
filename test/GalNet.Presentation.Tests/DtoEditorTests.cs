using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class DtoEditorTests
{
    [Test]
    public async Task MemberWireNameOverridesTypeAndWholeDtoCanComposeTheRemainingForm()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var dto = new UiDto { Title = "original", Other = "second", Count = 3 };
            using var data = new DtoBinding(dto);
            var registry = EditorRegistry.CreateDefault();
            registry.ReplaceDefault<string>(context => DefaultFieldEditors.CreateJson(context));
            registry.RegisterMember<UiDto>(value => value.Title, context => new(new TextBlock { Text = "member:" + context.Text }));
            DtoEditorContext? custom = null;
            registry.RegisterDto<UiDto>(context =>
            {
                custom = context;
                var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "custom DTO" }); panel.Children.Add(context.CreateDefaultForm("count"));
                return new(panel);
            });
            registry.Freeze();
            Assert.Throws<InvalidOperationException>(() => registry.ReplaceDefault<string>(DefaultFieldEditors.CreateText));
            var host = new DtoEditorHost { Item = new("first", typeof(UiDto), data.Fields, data.Binding), Registry = registry };
            var window = Mount(host);
            try
            {
                Assert.That(host.Diagnostics, Is.Empty);
                var form = host.GetVisualDescendants().OfType<SchemaForm>().Single();
                Assert.That(form.Editors.Select(item => item.Context.Field.Id), Is.EqualTo(new[] { "wire_title", "other" }));
                Assert.That(((TextBlock)form.Editors[0].Control).Text, Is.EqualTo("member:original"));
                Assert.That(form.Editors[1].Control, Is.TypeOf<TextBox>());
                Assert.That(await form.Contexts[1].CommitValueAsync("edited"), Is.True);
                Assert.That(dto.Other, Is.EqualTo("edited"));
                Assert.That(dto.Title, Is.EqualTo("original"));
                Assert.That(await custom!.CommitAsync(new Dictionary<string, FieldValue> { ["count"] = new(Json("8")) }), Is.Null);
                Assert.That(dto.Count, Is.EqualTo(8));
                Assert.That((await custom.CommitAsync(new Dictionary<string, FieldValue> { ["count"] = new(Json("9")), ["other"] = new(Json("\"bad\"")) }))?.Code, Is.EqualTo("dto.atomic"));
                Assert.That(dto.Count, Is.EqualTo(8), "Without a host atomic commit, no part of a multi-field patch may be written.");
            }
            finally { window.Close(); }
            Assert.That((await custom!.CommitAsync(new Dictionary<string, FieldValue> { ["count"] = new(Json("10")) }))?.Code, Is.EqualTo("dto.obsolete"));
            Assert.That(dto.Count, Is.EqualTo(8));
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task RepeatedDtoTypesRetainIndependentHostsAcrossMovesAndReleaseRemovedItems()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var fields = new[] { new FieldDescriptor("count", new(typeof(int))) };
            var first = new Binding("1"); var second = new Binding("2"); var third = new Binding("3");
            var items = new ObservableCollection<DtoEditorItem>
            {
                new("a", typeof(UiDto), fields, _ => first), new("b", typeof(UiDto), fields, _ => second)
            };
            var panel = new DtoPanel { ItemsSource = items, Registry = EditorRegistry.CreateDefault() };
            var window = Mount(panel);
            try
            {
                var a = (DtoEditorHost)panel.Children[0]; var b = (DtoEditorHost)panel.Children[1];
                var firstForm = (SchemaForm)a.Content!; var oldContext = firstForm.Contexts[0];
                Assert.That(first.Subscribers, Is.EqualTo(1)); Assert.That(second.Subscribers, Is.EqualTo(1));
                items.Move(0, 1); window.UpdateLayout();
                Assert.That(panel.Children[0], Is.SameAs(b)); Assert.That(panel.Children[1], Is.SameAs(a));
                Assert.That(a.Content, Is.SameAs(firstForm), "Reordering must not recreate the editor or lose its draft.");
                firstForm.Contexts[0].Text = "-";
                items.Add(new("c", typeof(UiDto), fields, _ => third)); window.UpdateLayout();
                Assert.That(firstForm.Contexts[0].Text, Is.EqualTo("-"));
                items.RemoveAt(1); window.UpdateLayout(); Assert.That(first.Subscribers, Is.Zero);
                Assert.That(oldContext.IsEnabled, Is.False);
                items.Add(new("c", typeof(UiDto), fields, _ => first));
                Assert.That(panel.Diagnostics.Single().Code, Is.EqualTo("dto.identity"));
                items.RemoveAt(items.Count - 1); Assert.That(panel.Diagnostics, Is.Empty);
            }
            finally { window.Close(); }
            Assert.That(first.Subscribers + second.Subscribers + third.Subscribers, Is.Zero);
            items.Clear(); Assert.That(panel.Children, Is.Empty, "Detached panels unsubscribe from their collection.");
        }, CancellationToken.None);
    }

    [Test]
    public async Task FactoryFailureCancelsFieldsAndDoesNotBreakOtherDtoItems()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var binding = new Binding("1"); FieldEditorContext? old = null;
            var registry = EditorRegistry.CreateDefault();
            registry.RegisterDto<UiDto>(context => { old = context.CreateField("count"); throw new InvalidOperationException("broken editor"); });
            var fields = new[] { new FieldDescriptor("count", new(typeof(int))) };
            var panel = new DtoPanel { Registry = registry, ItemsSource = [new("broken", typeof(UiDto), fields, _ => binding), new("default", null, fields, _ => binding)] };
            var window = Mount(panel);
            Assert.That(((DtoEditorHost)panel.Children[0]).Diagnostics.Single().Code, Is.EqualTo("dto.create"));
            Assert.That(((DtoEditorHost)panel.Children[1]).Content, Is.TypeOf<SchemaForm>());
            Assert.That(old!.IsEnabled, Is.False); Assert.That(binding.Subscribers, Is.EqualTo(1));
            window.Close(); Assert.That(binding.Subscribers, Is.Zero);
        }, CancellationToken.None);
    }

    [Test]
    public async Task AtomicDtoCommitUsesOneHostCallAndCancellationBelongsToTheMountedEditor()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            using var data = new DtoBinding(new UiDto()); var calls = 0; DtoEditorContext? context = null;
            var registry = EditorRegistry.CreateDefault();
            registry.RegisterDto<UiDto>(ctx => { context = ctx; return new(ctx.CreateDefaultForm()); });
            var pending = new TaskCompletionSource<ValueIssue?>(); CancellationToken token = default;
            var host = new DtoEditorHost { Registry = registry, Item = new("a", typeof(UiDto), data.Fields, data.Binding, (patch, cancellation) => { calls++; token = cancellation; return pending.Task; }) };
            var window = Mount(host);
            var commit = context!.CommitAsync(new Dictionary<string, FieldValue> { ["wire_title"] = new(Json("\"new\"")), ["count"] = new(Json("4")) });
            Assert.That(calls, Is.EqualTo(1)); window.Close(); Assert.That(token.IsCancellationRequested, Is.True);
            pending.SetResult(null); Assert.That((await commit)?.Code, Is.EqualTo("dto.obsolete"));
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public void RegistrationRejectsDuplicatesAndInvalidMemberExpressions()
    {
        var registry = EditorRegistry.CreateDefault();
        Assert.Throws<ArgumentException>(() => registry.RegisterDefault<string>(DefaultFieldEditors.CreateText));
        Assert.Throws<ArgumentException>(() => registry.RegisterDefault<int?>(DefaultFieldEditors.CreateNumeric));
        registry.RegisterMember<UiDto>(value => value.Title, DefaultFieldEditors.CreateText);
        Assert.Throws<ArgumentException>(() => registry.RegisterMember<UiDto>(value => value.Title, DefaultFieldEditors.CreateText));
        Assert.Throws<ArgumentException>(() => registry.RegisterMember<UiDto>(value => value.Title.Length, DefaultFieldEditors.CreateNumeric));
        registry.ReplaceMember<UiDto>(value => value.Title, DefaultFieldEditors.CreateJson);
        Assert.Throws<ArgumentException>(() => registry.ReplaceDto<UiDto>(context => new(context.CreateDefaultForm())));
        registry.RegisterDto<UiDto>(context => new(context.CreateDefaultForm()));
        Assert.Throws<ArgumentException>(() => registry.RegisterDto<UiDto>(context => new(context.CreateDefaultForm())));
    }

    [Test]
    public async Task ReleaseFailuresRemainDiagnosticsAndDoNotPreventOtherItemsFromBeingReleased()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch(() =>
        {
            var first = new Binding("1"); var second = new Binding("2"); var releases = 0;
            var registry = EditorRegistry.CreateDefault();
            registry.RegisterDto<UiDto>(context =>
            {
                context.CreateField("count");
                return new(new TextBlock(), () => { releases++; throw new IOException("release failed"); });
            });
            var fields = new[] { new FieldDescriptor("count", new(typeof(int))) };
            var panel = new DtoPanel { Registry = registry, ItemsSource = [new("a", typeof(UiDto), fields, _ => first), new("b", typeof(UiDto), fields, _ => second)] };
            var window = Mount(panel); var hosts = panel.Children.Cast<DtoEditorHost>().ToArray(); window.Close();
            Assert.That(releases, Is.EqualTo(2)); Assert.That(first.Subscribers + second.Subscribers, Is.Zero);
            Assert.That(hosts.All(host => host.Diagnostics.Single().Code == "dto.release"), Is.True);
        }, CancellationToken.None);
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static Window Mount(Control control)
    {
        var window = new Window { Content = control, Width = 800, Height = 600 };
        window.Styles.Add(new FluentTheme());
        window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/")) { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
        window.Show(); window.UpdateLayout(); return window;
    }
    private sealed class Binding(string json) : IFieldBinding
    {
        private EventHandler? _changed;
        public int Subscribers { get; private set; }
        public FieldValue Value { get; private set; } = new(Json(json));
        public FieldValue Read() => Value;
        public event EventHandler? Changed { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Value = value; _changed?.Invoke(this, EventArgs.Empty); return Task.FromResult<ValueIssue?>(null); }
    }
}

[GenerateFieldSchema]
public partial class UiDto
{
    [Field(0), JsonPropertyName("wire_title")] public string Title { get; set; } = "title";
    [Field(1)] public string Other { get; set; } = "other";
    [Field(2)] public int Count { get; set; }
}
