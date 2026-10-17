using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task EntryBatchFailuresRestoreContentAndHistoryOrRequireRepair()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "EntryBatchFailure"), "Entries", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        workspace.InsertEntriesInto(group, 0, 3, BuiltinEntryIds.Wait);
        var author = project.Services.GetRequiredService<GroupEntryAuthoringService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var original = group.Entries.ToArray(); var clip = author.Copy(group.Id, original.Select(entry => entry.StableId)); history.Clear();
        var shouldFail = false;
        NotifyCollectionChangedEventHandler failOnce = (_, _) => { if (shouldFail) { shouldFail = false; throw new IOException("collection observer failure"); } };
        group.Entries.CollectionChanged += failOnce;
        try
        {
            shouldFail = true; Assert.Throws<IOException>(() => author.Paste(group.Id, clip, null)); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
            shouldFail = true; Assert.Throws<IOException>(() => author.Delete(group.Id, [original[0].StableId, original[2].StableId])); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
            shouldFail = true; Assert.Throws<IOException>(() => author.Move(group.Id, [original[0].StableId], original[0].StableId, 2)); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
            var inserted = author.Paste(group.Id, clip, null); var withPaste = group.Entries.ToArray(); shouldFail = true;
            try { await history.UndoAsync(); Assert.Fail("Expected observer failure."); } catch (IOException) { }
            Assert.That(group.Entries, Is.EqualTo(withPaste)); Assert.That(history.CanUndo, Is.True); Assert.That(history.CanRedo, Is.False);
            await history.UndoAsync(); Assert.That(group.Entries, Is.EqualTo(original)); shouldFail = true;
            try { await history.RedoAsync(); Assert.Fail("Expected observer failure."); } catch (IOException) { }
            Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanRedo, Is.True); Assert.That(history.CanUndo, Is.False);
            await history.RedoAsync(); Assert.That(group.Entries.Skip(3).Select(entry => entry.StableId), Is.EqualTo(inserted.Select(entry => entry.StableId)));
        }
        finally { group.Entries.CollectionChanged -= failOnce; }
        history.Clear();
        NotifyCollectionChangedEventHandler failAlways = (_, _) => throw new IOException("persistent observer failure");
        group.Entries.CollectionChanged += failAlways;
        try { Assert.Throws<HistoryRepairRequiredException>(() => author.Paste(group.Id, clip, null)); Assert.That(history.RepairRequired, Is.True); Assert.That(project.Services.GetRequiredService<IProjectContentSession>().RepairRequired, Is.True); }
        finally { group.Entries.CollectionChanged -= failAlways; await projects.CloseAsync(); }
    }

    [Test]
    public async Task EntryClipboardPreservesDraftJsonAndBatchHistoryAcrossGroupsAndPageClose()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "EntryClipboard"), "Entries", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var source = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        workspace.InsertEntriesInto(source, 0, 4, BuiltinEntryIds.Wait);
        source.Entries[0].Condition = "score > 1";
        source.Entries[0].Parameters = new();
        var effect = workspace.InsertEntriesInto(source, 2, 1, BuiltinEntryIds.ApplyEffect).Single();
        effect.Parameters = new() { ["parameters"] = "{\"custom\":[1,null,{\"target\":\"" + source.Id + "\"}]}" };
        effect.Source.Parameters["instanceId"] = JsonSerializer.SerializeToElement<object?>(null);
        var target = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 600, 300);
        var author = project.Services.GetRequiredService<GroupEntryAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var original = source.Entries.ToArray();
        var clip = author.Copy(source.Id, [effect.StableId, original[0].StableId]);
        original[0].Condition = "changed";
        history.Clear();
        var pasted = author.Paste(target.Id, clip, null);
        Assert.That(pasted.Select(entry => entry.Type), Is.EqualTo(new[] { BuiltinEntryIds.Wait, BuiltinEntryIds.ApplyEffect }));
        Assert.That(pasted[0].Condition, Is.EqualTo("score > 1"));
        Assert.That(pasted[0].Source.Parameters.ContainsKey("duration"), Is.False);
        Assert.That(pasted[1].Source.Parameters["instanceId"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(pasted[1].Source.Parameters["parameters"].GetProperty("custom")[2].GetProperty("target").GetString(), Is.EqualTo(source.Id));
        Assert.That(pasted.All(entry => original.All(item => item.StableId != entry.StableId)), Is.True);
        using (var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, target.Id))
            Assert.That(page.GroupNode.Entries, Has.Count.EqualTo(2));
        await history.UndoAsync(); Assert.That(target.Entries, Is.Empty); Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync(); Assert.That(target.Entries.Select(entry => entry.StableId), Is.EqualTo(pasted.Select(entry => entry.StableId)));
        author.Delete(source.Id, [original[3].StableId, original[0].StableId]);
        Assert.That(source.Entries.Count, Is.EqualTo(3));
        await history.UndoAsync(); Assert.That(source.Entries, Is.EqualTo(original)); Assert.That(source.Entries.Select(entry => entry.Id), Is.EqualTo(Enumerable.Range(1, 5)));
        await history.UndoAsync(); Assert.That(target.Entries, Is.Empty); Assert.That(history.CanUndo, Is.False, "History follows project order across Group pages.");
        await history.RedoAsync(); await history.RedoAsync(); Assert.That(source.Entries.Count, Is.EqualTo(3));
        await workspace.SaveAsync(); var path = project.RootPath; var targetId = target.Id;
        await projects.CloseAsync(); var reopened = await projects.OpenAsync(path);
        var saved = reopened.Services.GetRequiredService<IEditorDocumentService>().Groups[targetId].Entries;
        Assert.That(saved[0].Parameters.ContainsKey("duration"), Is.False);
        Assert.That(saved[1].Parameters["instanceId"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(saved[1].Parameters["parameters"].GetProperty("custom")[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
        await projects.CloseAsync();
    }

    [Test]
    public async Task EntryClipboardRejectsWholeMalformedPayloadWithoutHistoryAndPreservesUnknownSchema()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "EntryRejected"), "Entries", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
        var author = project.Services.GetRequiredService<GroupEntryAuthoringService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var clip = author.Copy(group.Id, [entry.StableId]); history.Clear();
        var unsupported = JsonNode.Parse(clip)!; unsupported["Entries"]!.AsArray().Add(JsonNode.Parse("{\"Type\":\"unavailable.custom\",\"Condition\":\"\",\"Parameters\":{}}"));
        var unknownField = JsonNode.Parse(clip)!; unknownField["Entries"]![0]!["Parameters"]!["newField"] = 1;
        var malformed = JsonNode.Parse(clip)!; malformed["Entries"]![0]!["Parameters"] = 42;
        var invalid = new[] { "", "not json", "null", "{}", clip.Replace("GalNet.Entries", "GalNet.Graph", StringComparison.Ordinal), clip.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal), malformed.ToJsonString(), new string('x', 16 * 1024 * 1024 + 1) };
        foreach (var text in invalid)
        {
            Assert.Throws<EditorOperationException>(() => author.Paste(group.Id, text, null));
            Assert.That(group.Entries.Single(), Is.SameAs(entry)); Assert.That(history.CanUndo, Is.False);
        }
        Assert.Throws<EditorOperationException>(() => author.Paste(group.Id, clip, "deleted-anchor"));
        Assert.That(history.CanUndo, Is.False);
        var pasted = author.Paste(group.Id, unsupported.ToJsonString(), null);
        Assert.That(pasted[^1].Type, Is.EqualTo("unavailable.custom")); Assert.That(pasted[^1].IsUnknownType, Is.True);
        await history.UndoAsync(); Assert.That(group.Entries.Single(), Is.SameAs(entry));
        pasted = author.Paste(group.Id, unknownField.ToJsonString(), null);
        Assert.That(pasted.Single().Source.Parameters["newField"].GetInt32(), Is.EqualTo(1)); Assert.That(pasted.Single().HasUnknownParameters, Is.True);
        await history.UndoAsync(); Assert.That(group.Entries.Single(), Is.SameAs(entry));
        await projects.CloseAsync();
    }

    [Test]
    public async Task EntryNativeMultiSelectionKeyboardClipboardAndContextMenuRespectInputs()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EntryMultiUi"), "Entries", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            workspace.InsertEntriesInto(group, 0, 5, BuiltinEntryIds.Wait);
            using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            foreach (var entry in group.Entries) entry.IsParametersExpanded = false;
            var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1100, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var list = view.FindControl<ListBox>("ItemsListBox")!; var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear(); var original = group.Entries.ToArray();
                ClickEntryRow(window, list, 0); ClickEntryRow(window, list, 2, RawInputModifiers.Control);
                Assert.That(vm.SelectedEntries, Is.EquivalentTo(new[] { original[0], original[2] })); Assert.That(vm.SelectedEntry, Is.SameAs(original[2]));
                ClickEntryRow(window, list, 4, RawInputModifiers.Shift); Assert.That(vm.SelectedEntries, Does.Contain(original[3]).And.Contain(original[4]));
                vm.SetSelection([original[2], original[0]]); await view.CopySelectionAsync();
                var text = await window.Clipboard!.TryGetTextAsync(); Assert.That(JsonNode.Parse(text!)!["Entries"]![0]!["StableId"]!.GetValue<string>(), Is.EqualTo(original[0].StableId));
                vm.SelectedEntry = original[2]; await view.PasteSelectionAsync();
                Assert.That(group.Entries, Has.Count.EqualTo(7)); Assert.That(vm.SelectedEntries, Has.Count.EqualTo(2)); Assert.That(group.Entries[3], Is.SameAs(vm.SelectedEntries[0]));
                await history.UndoAsync(); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                vm.SetSelection([original[1], original[3]]); list.Focus(); SendEntryKey(list, Key.D, KeyModifiers.Control); Dispatcher.UIThread.RunJobs();
                Assert.That(group.Entries, Has.Count.EqualTo(7)); await history.UndoAsync(); Assert.That(group.Entries, Is.EqualTo(original));
                vm.SetSelection([original[1], original[3]]); list.Focus(); SendEntryKey(list, Key.Delete); Assert.That(group.Entries, Has.Count.EqualTo(3));
                await history.UndoAsync(); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                var numeric = view.FindControl<NumericUpDown>("BatchCount")!; var value = vm.BatchAddCount; SendEntryKey(numeric, Key.D, KeyModifiers.Control); Assert.That(group.Entries.Count, Is.EqualTo(5)); Assert.That(vm.BatchAddCount, Is.EqualTo(value));
                vm.SelectedEntry = original[0]; original[0].IsParametersExpanded = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var input = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<TextBox>().First(); input.Text = "text"; input.SelectAll(); SendEntryKey(input, Key.Delete); Assert.That(group.Entries.Count, Is.EqualTo(5), "Entry delete must not consume text editing.");
                var row = list.ContainerFromIndex(2)!; var context = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent }; row.RaiseEvent(context); Assert.That(context.Handled, Is.True);
                Assert.That(vm.SelectedEntry, Is.SameAs(original[2]));
                var menu = window.GetVisualDescendants().OfType<MenuItem>().FirstOrDefault(item => Equals(item.Header, vm.Workspace.Localize("Entry.Menu.Copy")));
                Assert.That(menu, Is.Not.Null, "The real context menu is mounted.");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); vm.SetSelection([]); list.Focus(); SendEntryKey(list, Key.A, KeyModifiers.Control); Assert.That(vm.SelectedEntries.Count, Is.EqualTo(group.Entries.Count));
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "group-multiselect.png")); }
            }
            finally { window.Close(); vm.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryCutAndPasteIgnoreFailedStaleAndClosedPageClipboardCompletions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EntryAsyncClipboard"), "Entries", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            workspace.InsertEntriesInto(group, 0, 3, BuiltinEntryIds.Wait);
            using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var view = new ControlledEntryClipboardView { DataContext = vm }; var window = new Window { Content = view, Width = 1000, Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear(); var original = group.Entries.ToArray(); vm.SetSelection([original[0], original[2]]);
                view.Write = _ => Task.FromException(new IOException("clipboard busy")); await view.CopySelectionAsync(cut: true); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                var pending = new TaskCompletionSource(); view.Write = _ => pending.Task;
                var cut = view.CopySelectionAsync(cut: true); original[0].Condition = "changed while copying"; pending.SetResult(); await cut;
                Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(vm.ErrorText, Is.Not.Empty); history.Clear();
                view.Write = _ => Task.CompletedTask; await view.CopySelectionAsync(cut: true); Assert.That(group.Entries, Has.Count.EqualTo(1)); await history.UndoAsync(); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                vm.SelectedEntry = original[0]; var clip = vm.CopySelection(); var read = new TaskCompletionSource<string?>(); view.Read = () => read.Task; var paste = view.PasteSelectionAsync();
                vm.SelectedEntry = original[2]; read.SetResult(clip); await paste; Assert.That(group.Entries[1].StableId, Is.Not.EqualTo(original[1].StableId), "Paste uses the captured anchor, not the later selection.");
                await history.UndoAsync(); history.Clear(); vm.SelectedEntry = original[0]; read = new(); view.Read = () => read.Task; paste = view.PasteSelectionAsync();
                workspace.RemoveEntryFrom(group, original[0]); var count = group.Entries.Count; read.SetResult(clip); await paste; Assert.That(group.Entries.Count, Is.EqualTo(count)); Assert.That(vm.ErrorText, Is.Not.Empty);
                var localization = project.Services.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" }) { localization.ApplyLocale(locale); Assert.That(vm.ErrorText, Is.EqualTo(localization["Error.entry.anchorMissing"])); }
                vm.SelectedEntry = group.Entries[0]; read = new(); view.Read = () => read.Task; paste = view.PasteSelectionAsync(); vm.Dispose(); read.SetResult(clip); await paste; Assert.That(group.Entries.Count, Is.EqualTo(count));
            }
            finally { window.Close(); vm.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryMultiDragPreservesOrderFoldingAndOneHistoryStep()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EntryMultiDrag"), "Entries", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            workspace.InsertEntriesInto(group, 0, 5, BuiltinEntryIds.Wait); using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            foreach (var entry in group.Entries) entry.IsParametersExpanded = false;
            var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1000, Height = 750 }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear(); var original = group.Entries.ToArray(); vm.SetSelection([original[0], original[2]]);
                var handle = view.GetVisualDescendants().OfType<Border>().Single(border => border.Child is TextBlock { Text: "::" } && ReferenceEquals(border.DataContext, original[0]));
                var list = view.FindControl<ListBox>("ItemsListBox")!; var target = list.ContainerFromIndex(4)!;
                var start = handle.TranslatePoint(new Point(8, 8), window)!.Value; var end = target.TranslatePoint(new Point(20, target.Bounds.Height - 5), window)!.Value;
                window.MouseDown(start, MouseButton.Left); window.MouseMove(end); window.MouseUp(end, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                Assert.That(group.Entries, Is.EqualTo(new[] { original[1], original[3], original[4], original[0], original[2] })); Assert.That(vm.SelectedEntries, Has.Count.EqualTo(2)); Assert.That(original[0].IsParametersExpanded, Is.False);
                await history.UndoAsync(); Assert.That(group.Entries, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); Assert.That(group.Entries[^1], Is.SameAs(original[2]));
                history.Clear(); project.Services.GetRequiredService<GroupEntryAuthoringService>().Move(group.Id, [original[0].StableId, original[2].StableId], original[2].StableId, 0);
                Assert.That(group.Entries, Is.EqualTo(new[] { original[0], original[2], original[1], original[3], original[4] })); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False);
            }
            finally { window.Close(); vm.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    private static void ClickEntryRow(Window window, ListBox list, int index, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        list.ScrollIntoView(list.Items[index]!); window.UpdateLayout(); var row = list.ContainerFromIndex(index)!;
        var point = row.TranslatePoint(new Point(75, 20), window)!.Value; window.MouseDown(point, MouseButton.Left, modifiers); window.MouseUp(point, MouseButton.Left, modifiers); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void SendEntryKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) => target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private sealed class ControlledEntryClipboardView : EntryListEditorControl
    {
        public Func<string, Task> Write { get; set; } = _ => Task.CompletedTask;
        public Func<Task<string?>> Read { get; set; } = () => Task.FromResult<string?>(null);
        protected override Task WriteClipboardAsync(string text) => Write(text);
        protected override Task<string?> ReadClipboardAsync() => Read();
    }
}
