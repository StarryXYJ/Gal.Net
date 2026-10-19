using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task GroupTransformAndColorUseRealControlsSingleHistoryAndLosslessSave()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); Application.Current!.Styles.Add(new Semi.Avalonia.ColorPicker.ColorPickerSemiTheme());
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "PropertyEditors"), "Properties", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowColorLayer).Single();
            entry.Source.Parameters["transform"] = JsonSerializer.Deserialize<JsonElement>("{\"x\":2,\"opaque\":{\"n\":9007199254740993}}");
            entry.Source.Parameters["color"] = JsonSerializer.SerializeToElement("#80112233");
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1000, Height = 760 }; window.Show(); TimelinePump(window);
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var transform = view.GetVisualDescendants().OfType<Grid>().Single(item => item.Name == "TransformEditor");
                Assert.That(transform.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "RepairProperty").IsEffectivelyVisible, Is.False);
                var x = transform.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "TransformX");
                var scale = transform.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "TransformScaleX");
                Assert.That(x.TranslatePoint(default, transform)!.Value.X, Is.EqualTo(scale.TranslatePoint(default, transform)!.Value.X));
                Assert.That(scale.Text, Is.EqualTo("1")); Assert.That(history.CanUndo, Is.False);
                scale.Text = "1.25";
                var z = view.GetVisualDescendants().OfType<SchemaForm>().SelectMany(item => item.Contexts).Single(item => item.Field.Id == "z");
                Assert.That(await z.CommitValueAsync(1001f), Is.True); Assert.That(scale.Text, Is.EqualTo("1.25"), "An unrelated field update must preserve the transform draft.");
                PressGroupKey(scale, Key.Escape); await history.UndoAsync(); history.Clear();
                var row = view.GetVisualDescendants().OfType<FieldEditorRow>().Single(item => item.Context?.Field.Id == "transform");
                var label = row.GetVisualDescendants().OfType<ContentControl>().Single(item => item.Name == "FieldLabel");
                var reset = row.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "UseDefault");
                Assert.That(label.TranslatePoint(default, row)!.Value.Y, Is.LessThan(15));
                Assert.That(reset.TranslatePoint(default, row)!.Value.Y, Is.LessThan(15));
                Assert.That(reset.TranslatePoint(default, row)!.Value.X, Is.GreaterThan(transform.TranslatePoint(default, row)!.Value.X + transform.Bounds.Width - 1));
                Assert.That(view.GetVisualDescendants().OfType<Button>().Where(item => item.IsEffectivelyVisible).Any(item => item.Name is "SetNull" or "OmitField" or "RevertDraft"), Is.False);
                x.Text = "-20"; PressGroupKey(x, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["transform"].GetProperty("x").GetSingle(), Is.EqualTo(-20));
                Assert.That(entry.Source.Parameters["transform"].TryGetProperty("X", out _), Is.False);
                Assert.That(entry.Source.Parameters["transform"].GetProperty("opaque").GetProperty("n").GetRawText(), Is.EqualTo("9007199254740993"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["transform"].GetProperty("x").GetSingle(), Is.EqualTo(2));
                await history.RedoAsync();
                x.Text = ""; PressGroupKey(x, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["transform"].TryGetProperty("x", out _), Is.False);
                Assert.That(x.Text, Is.EqualTo("0")); await history.UndoAsync();
                Assert.That(entry.Source.Parameters["transform"].GetProperty("x").GetSingle(), Is.EqualTo(-20)); history.Clear();
                var pick = view.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "PickColor");
                Click(window, pick); TimelinePump(window);
                var popup = (StackPanel)((Flyout)pick.Flyout!).Content!;
                var picker = popup.GetVisualDescendants().OfType<ColorView>().Single();
                Assert.That(picker.GetVisualDescendants().OfType<ColorSpectrum>().Any(), Is.True, "The themed color spectrum must actually exist.");
                picker.Color = Color.Parse("#40203040"); Assert.That(history.CanUndo, Is.False);
                popup.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "CancelColor").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(entry.Source.Parameters["color"].GetString(), Is.EqualTo("#80112233"));
                Click(window, pick); TimelinePump(window); picker.Color = Color.Parse("#40203040");
                popup.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ApplyColor").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); TimelinePump(window);
                Assert.That(entry.Source.Parameters["color"].GetString(), Is.EqualTo("#40203040"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters["color"].GetString(), Is.EqualTo("#80112233"));
                await history.RedoAsync();
                var hex = view.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "ColorHex");
                hex.Text = "#invalid"; PressGroupKey(hex, Key.Enter);
                Assert.That(entry.Source.Parameters["color"].GetString(), Is.EqualTo("#40203040"));
                PressGroupKey(hex, Key.Escape); Assert.That(hex.Text, Is.EqualTo("#40203040"));
                var locale = project.Services.GetRequiredService<IEditorLocalizationService>();
                foreach (var language in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    locale.ApplyLocale(language); TimelinePump(window);
                    Assert.That(view.GetVisualDescendants().OfType<FieldEditorActions>().First().DefaultText, Is.EqualTo(locale["Form.Reset"]));
                    Assert.That(transform.GetVisualDescendants().OfType<TextBlock>().Any(item => item.Text == locale["Form.Transform.ScaleX"]), Is.True);
                }
                CapturePropertyFrame(window, "group-properties-wide.png");
                window.Width = 520; TimelinePump(window);
                Assert.That(row.Bounds.Width, Is.LessThan(480));
                Assert.That(transform.TranslatePoint(default, row)!.Value.Y, Is.GreaterThan(label.Bounds.Height));
                CapturePropertyFrame(window, "group-properties-narrow.png");
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var id = entry.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == id);
                Assert.That(reopened.Source.Parameters["color"].GetString(), Is.EqualTo("#40203040"));
                Assert.That(reopened.Source.Parameters["transform"].GetProperty("x").GetSingle(), Is.EqualTo(-20));
                Assert.That(reopened.Source.Parameters["transform"].GetProperty("opaque").GetProperty("n").GetRawText(), Is.EqualTo("9007199254740993"));
            }
            finally { window.Close(); page.Dispose(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TimelinePropertyOpensItsOwnEntryAndReopensDockWithoutCreatingMissingData()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineProperty"), "Timeline", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayTimeline).Single(); entry.Source.Parameters.Remove("timeline");
            var other = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Wait).Single();
            var factory = project.Services.GetRequiredService<EditorDockFactory>(); var layout = factory.CreateLayout(); factory.InitLayout(layout); factory.OpenGroupEditor(group);
            var page = TimelineDockables(layout).Select(item => item.Context).OfType<GroupEditorPanelViewModel>().Single();
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1000, Height = 750 }; window.Show(); TimelinePump(window);
            try
            {
                page.SelectedEntry = other; var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var open = view.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "OpenTimeline");
                var timelineRow = view.GetVisualDescendants().OfType<FieldEditorRow>().Single(item => item.Context?.Field.Id == "timeline");
                Assert.That(timelineRow.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "UseDefault").IsEnabled, Is.False);
                Assert.That(await timelineRow.Context!.ResetAsync(), Is.False);
                factory.ToggleGlobalPanel(EditorDockPanelIds.Timeline);
                Click(window, open); TimelinePump(window);
                Assert.That(page.SelectedEntry, Is.SameAs(entry));
                Assert.That(project.Services.GetRequiredService<EntrySelectionContext>().Current?.EntryId, Is.EqualTo(entry.StableId));
                Assert.That(TimelineDockables(layout).Count(item => item.Id == EditorDockPanelIds.Timeline), Is.EqualTo(1));
                Assert.That(project.Services.GetRequiredService<TimelineEditSession>().CanCreate, Is.True);
                Assert.That(entry.Source.Parameters.ContainsKey("timeline"), Is.False); Assert.That(history.CanUndo, Is.False);
                page.DeleteEntries([entry.StableId]); open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(project.Services.GetRequiredService<EntrySelectionContext>().Current?.EntryId, Is.Not.EqualTo(entry.StableId));
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    private static void CapturePropertyFrame(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is not { Length: > 0 } path) return;
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(path, name));
    }

    [Test]
    public async Task CompactInputsDistinguishEmptyStringAbsentNullDefaultAndMissingRequired()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); var registry = EditorFieldEditors.CreateDefault();
            var optional = new PropertyBinding("7"); var required = new PropertyBinding("8"); var text = new PropertyBinding("\"hello\""); var nullable = new PropertyBinding("null");
            var withDefault = new PropertyBinding("9");
            var contexts = new[]
            {
                new FieldEditorContext(new("optional", new(typeof(int))), optional, registry),
                new FieldEditorContext(new("required", new(typeof(int)), isRequired: true), required, registry),
                new FieldEditorContext(new("text", new(typeof(string))), text, registry),
                new FieldEditorContext(new("nullable", new(typeof(int?), allowNull: true)), nullable, registry),
                new FieldEditorContext(new("default", new(typeof(int)), defaultValue: JsonSerializer.SerializeToElement(5)), withDefault, registry)
            };
            var leases = contexts.Select(context => registry.Resolve(context.Field)(context)).ToArray();
            var rows = contexts.Select((context, index) => new FieldEditorRow { Context = context, Label = context.Field.Id, Editor = leases[index].Control }).ToArray();
            var panel = new StackPanel(); foreach (var row in rows) panel.Children.Add(row);
            var window = new Window { Content = panel, Width = 700, Height = 500 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(nullable.Commits, Is.Zero); Assert.That(nullable.Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
                contexts[0].Text = ""; Assert.That(await contexts[0].CommitTextAsync(), Is.True); Assert.That(optional.Value.IsProvided, Is.False);
                contexts[1].Text = ""; Assert.That(await contexts[1].CommitTextAsync(), Is.False); Assert.That(required.Value.Json!.Value.GetInt32(), Is.EqualTo(8));
                contexts[2].Text = ""; Assert.That(await contexts[2].CommitTextAsync(), Is.True); Assert.That(text.Value.Json!.Value.GetString(), Is.Empty);
                var disabledReset = rows[1].GetVisualDescendants().OfType<Button>().Single(item => item.Name == "UseDefault"); Assert.That(disabledReset.IsEnabled, Is.False);
                contexts[4].Text = "-";
                rows[4].GetVisualDescendants().OfType<Button>().Single(item => item.Name == "UseDefault").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(withDefault.Value.Json!.Value.GetInt32(), Is.EqualTo(5)); Assert.That(withDefault.Commits, Is.EqualTo(1)); Assert.That(contexts[4].HasDraft, Is.False);
                rows[3].GetVisualDescendants().OfType<Button>().Single(item => item.Name == "UseDefault").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(nullable.Value.IsProvided, Is.False);
            }
            finally { window.Close(); foreach (var context in contexts) context.Dispose(); foreach (var lease in leases) lease.Dispose(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class PropertyBinding(string json) : IFieldBinding
    {
        public FieldValue Value { get; private set; } = new(JsonSerializer.Deserialize<JsonElement>(json));
        public int Commits { get; private set; }
        public FieldValue Read() => Value;
        public event EventHandler? Changed;
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Value = value; Commits++; Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult<ValueIssue?>(null);
        }
    }

    [Test]
    public async Task TransformListUsesRegisteredElementControlAndRejectsReorderedEditors()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); var binding = new PropertyBinding("[{\"X\":1,\"opaque\":true},{\"X\":2}]");
            var field = new FieldDescriptor("items", new(typeof(List<LayerTransform>)));
            var form = new SchemaForm { Schema = [field], Registry = EditorFieldEditors.CreateDefault(), Binding = _ => binding };
            var window = new Window { Content = form, Width = 800, Height = 700 };
            window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/")) { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
            window.Show(); TimelinePump(window);
            try
            {
                var list = (StructuredFieldEditor)form.Editors.Single().Control; list.IsExpanded = true; TimelinePump(window);
                var inputs = list.GetVisualDescendants().OfType<TextBox>().Where(item => item.Name == "TransformX").ToArray();
                Assert.That(inputs, Has.Length.EqualTo(2));
                inputs[0].Text = "3"; PressGroupKey(inputs[0], Key.Enter); TimelinePump(window);
                Assert.That(binding.Value.Json!.Value[0].GetProperty("X").GetSingle(), Is.EqualTo(3));
                Assert.That(binding.Value.Json.Value[0].GetProperty("opaque").GetBoolean(), Is.True);
                list.GetVisualDescendants().OfType<Button>().First(item => item.Name == "down").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); TimelinePump(window);
                Assert.That(binding.Value.Json.Value[0].GetProperty("X").GetSingle(), Is.EqualTo(2));
                inputs[0].Text = "99"; PressGroupKey(inputs[0], Key.Enter);
                Assert.That(binding.Value.Json.Value[0].GetProperty("X").GetSingle(), Is.EqualTo(2));
                Assert.That(binding.Commits, Is.EqualTo(2));
                list.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "add").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); TimelinePump(window);
                var added = list.GetVisualDescendants().OfType<TextBox>().Last(item => item.Name == "TransformX");
                Assert.That(added.Text, Is.EqualTo("0")); added.Text = "4"; PressGroupKey(added, Key.Enter);
                Assert.That(binding.Value.Json.Value[2].GetProperty("X").GetSingle(), Is.EqualTo(4));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CompactObjectOffersRepairWhenAnotherInvalidMemberBlocksLocalEditing()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); var binding = new PropertyBinding("{\"Count\":\"bad\",\"Name\":3,\"opaque\":true}");
            var field = new FieldDescriptor("details", new(typeof(DocumentOptions)));
            var form = new SchemaForm { Schema = [field], Registry = EditorFieldEditors.CreateDefault(), Binding = _ => binding };
            var window = new Window { Content = form, Width = 800, Height = 700 };
            window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Avalonia.Controls/")) { Source = new Uri("avares://GalNet.Avalonia.Controls/Styles/SchemaForm.axaml") });
            window.Show(); TimelinePump(window);
            try
            {
                var editor = (StructuredFieldEditor)form.Editors.Single().Control; editor.RepairText = "Repair"; editor.IsExpanded = true; TimelinePump(window);
                var repair = editor.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "JsonMode"); Assert.That(repair.IsVisible, Is.False);
                var name = editor.GetVisualDescendants().OfType<SchemaForm>().Single().Contexts.Single(item => item.Field.Id == "name");
                name.Text = "repaired"; Assert.That(await name.CommitTextAsync(), Is.False); Assert.That(binding.Commits, Is.Zero);
                Assert.That(repair.IsVisible, Is.True, "The root validation error must reveal the source repair path.");
                repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); TimelinePump(window);
                var source = editor.GetVisualDescendants().OfType<TextBox>().Single(); source.Text = "{\"Count\":1,\"Name\":\"repaired\",\"opaque\":true}";
                source.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
                Assert.That(binding.Commits, Is.EqualTo(1)); Assert.That(binding.Value.Json!.Value.GetProperty("Count").GetInt32(), Is.EqualTo(1));
                Assert.That(binding.Value.Json.Value.GetProperty("opaque").GetBoolean(), Is.True);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }
}
