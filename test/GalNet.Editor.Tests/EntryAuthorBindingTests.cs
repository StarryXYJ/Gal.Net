using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task EntryFieldsUseProjectOrderAcrossMountedPagesAndSurvivePageCloseAndReopen()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EntryBindings"), "Bindings", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var first = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var second = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 200);
            var text = workspace.InsertEntriesInto(first, 0, 1, BuiltinEntryIds.Text).Single();
            var wait = workspace.InsertEntriesInto(second, 0, 1, BuiltinEntryIds.Wait).Single();
            var baseline = wait.Parameters;
            using var page1 = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, first.Id);
            using var page2 = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, second.Id);
            var view1 = new EntryListEditorControl { DataContext = page1 }; var view2 = new EntryListEditorControl { DataContext = page2 };
            var window1 = new Window { Content = view1, Width = 1100, Height = 700 }; var window2 = new Window { Content = view2, Width = 1100, Height = 700 };
            window1.Show(); window2.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var condition = view1.GetVisualDescendants().OfType<ConditionEditorControl>().First();
                condition.FindControl<TextBox>("EditorBox")!.Text = "true"; Dispatcher.UIThread.RunJobs();
                Assert.That(text.Source.Condition, Is.EqualTo("true"));
                var form = view2.GetVisualDescendants().OfType<SchemaForm>().Single(item => item.Schema?.Any(field => field.Id == "duration") == true);
                var field = form.Contexts.Single(context => context.Field.Id == "duration");
                ((TextBox)form.Editors.Single(editor => editor.Context == field).Control).Text = "4.25"; Dispatcher.UIThread.RunJobs();
                Assert.That(await field.CommitTextAsync(), Is.True); Assert.That(wait.Source.Parameters["duration"].GetSingle(), Is.EqualTo(4.25f));
                var content = view1.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "GameTextInput");
                content.Text = "Written through the author intent"; Dispatcher.UIThread.RunJobs();
                content.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
                var texts = project.Services.GetRequiredService<GameTextAuthoringService>();
                await PumpCharacterUi(() => texts.Read(text.Source.Parameters["content"].GetString()!) == "Written through the author intent");
                var contentKey = text.Source.Parameters["content"].GetString()!;
                Assert.That(contentKey, Is.Not.EqualTo(content.Text));
                var oldForm = wait.ParameterForm!;
                window1.Content = null; window2.Content = null; page1.Dispose(); page2.Dispose();
                Assert.That(await oldForm.Binding(oldForm.Fields.First()).CommitAsync(new(JsonSerializer.SerializeToElement(8)), CancellationToken.None), Is.Not.Null);
                await history.UndoAsync(); Assert.That(text.Parameters.GetValueOrDefault("content", ""), Is.Empty);
                await history.UndoAsync(); Assert.That(wait.Parameters, Is.EquivalentTo(baseline));
                await history.UndoAsync(); Assert.That(text.Condition, Is.Empty); Assert.That(history.CanUndo, Is.False, "Each field edit is exactly one project history step.");
                await history.RedoAsync(); await history.RedoAsync(); await history.RedoAsync();
                await workspace.SaveAsync(); var path = project.RootPath; var firstId = first.Id; var secondId = second.Id; var stableId = text.StableId;
                window1.Close(); window2.Close(); await projects.CloseAsync();
                var reopened = await projects.OpenAsync(path); var groups = reopened.Services.GetRequiredService<EditorGroupProjectionStore>();
                Assert.That(groups.Get(firstId).Entries.Single().StableId, Is.EqualTo(stableId));
                Assert.That(groups.Get(firstId).Entries.Single().Condition, Is.EqualTo("true"));
                Assert.That(groups.Get(firstId).Entries.Single().Parameters["content"], Is.EqualTo(contentKey));
                Assert.That(reopened.Services.GetRequiredService<GameTextAuthoringService>().Read(contentKey), Is.EqualTo("Written through the author intent"));
                Assert.That(groups.Get(secondId).Entries.Single().Source.Parameters["duration"].GetSingle(), Is.EqualTo(4.25f));
                await projects.CloseAsync();
            }
            finally { window1.Close(); window2.Close(); page1.Dispose(); page2.Dispose(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryBindingsPreserveAuthorJsonAndRejectRemovedAndReplacedSchemas()
    {
        using var histories = new EditorHistories(); var documents = new EditorDocumentService();
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.value",
            new DynamicParameterTable([new("count", typeof(int)), new("optional", typeof(int?), allowNull: true)]))])]);
        var source = new EditorEntryData
        {
            StableId = "line",
            Type = "custom.value",
            Parameters = new()
            { ["optional"] = JsonSerializer.SerializeToElement<int?>(null), ["future"] = JsonSerializer.SerializeToElement(new { preserved = new[] { 1, 2 } }) }
        };
        documents.Load(new() { Document = new() { Nodes = [new() { Id = "group", Type = "Group", File = "groups/group.rawgalgroup" }] }, GroupEntries = { ["group"] = [source] } });
        var authoring = new EntryFieldAuthoringService(documents, histories); using var store = new EditorGroupProjectionStore(documents, catalog, authoring);
        var group = store.Get("group"); var entry = group.Entries.Single(); var original = JsonSerializer.SerializeToElement(source.Parameters);
        entry.ConfigureParameterFields(catalog, [], []);
        Assert.That(JsonElement.DeepEquals(original, JsonSerializer.SerializeToElement(source.Parameters)), Is.True); Assert.That(histories.Content.CanUndo, Is.False);
        var field = (IFieldBinding)entry.ParameterFields.Single(item => item.Id == "count");
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(7)), CancellationToken.None), Is.Null);
        Assert.That(source.Parameters["future"].GetProperty("preserved").GetArrayLength(), Is.EqualTo(2)); Assert.That(source.Parameters["optional"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        await histories.Content.UndoAsync(); Assert.That(JsonElement.DeepEquals(original, JsonSerializer.SerializeToElement(source.Parameters)), Is.True);
        authoring.SetType("group", source, "future.unknown", new() { ["payload"] = JsonSerializer.SerializeToElement(new { nested = true }) });
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null);
        await histories.Content.UndoAsync(); entry.ConfigureParameterFields(catalog, [], []);
        Assert.That(JsonElement.DeepEquals(original, JsonSerializer.SerializeToElement(source.Parameters)), Is.True);
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null, "Undoing a type change must not revive an old field lease.");
        field = (IFieldBinding)entry.ParameterFields.Single(item => item.Id == "count");
        var batch = new GroupEntryAuthoringService(store, documents, catalog, histories); histories.Clear(); batch.Delete("group", [entry.StableId]);
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null);
        await histories.Content.UndoAsync(); entry.ConfigureParameterFields(catalog, [], []);
        Assert.That(await field.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null, "Undoing removal must not revive the old control lease.");
        var current = (IFieldBinding)entry.ParameterFields.Single(item => item.Id == "count");
        documents.Unload(); Assert.That(await current.CommitAsync(new(JsonSerializer.SerializeToElement(9)), CancellationToken.None), Is.Not.Null);
    }

    [TestCase(false), TestCase(true)]
    public void EntryFieldNotificationsCompensateBeforeHistoryMoves(bool persistent)
    {
        using var histories = new EditorHistories(); var documents = new EditorDocumentService();
        var source = new EditorEntryData { StableId = "line", Type = "custom.unknown" };
        documents.Load(new() { Document = new() { Nodes = [new() { Id = "group", Type = "Group", File = "groups/group.rawgalgroup" }] }, GroupEntries = { ["group"] = [source] } });
        var authoring = new EntryFieldAuthoringService(documents, histories); var fail = true;
        void Fail(string _) { if (persistent || fail) { fail = false; throw new IOException("entry observer"); } }
        authoring.Changed += Fail;
        try
        {
            if (persistent) Assert.Throws<HistoryRepairRequiredException>(() => authoring.SetCondition("group", source, "true"));
            else Assert.Throws<IOException>(() => authoring.SetCondition("group", source, "true"));
            Assert.That(source.Condition, Is.Empty); Assert.That(histories.Content.CanUndo, Is.False); Assert.That(histories.Content.RepairRequired, Is.EqualTo(persistent));
        }
        finally { authoring.Changed -= Fail; }
        if (!persistent)
        {
            authoring.SetCondition("group", source, "true"); source.Condition = "outside";
            Assert.Throws<EditorOperationException>(() => histories.Content.Undo());
            Assert.That(histories.Content.CanUndo, Is.True); Assert.That(source.Condition, Is.EqualTo("outside"));
        }
    }
}
