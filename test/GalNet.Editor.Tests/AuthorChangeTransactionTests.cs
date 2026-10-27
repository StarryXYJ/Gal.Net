using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Threading;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class AuthorChangeTransactionTests
{
    [Test]
    public async Task SceneQueryPublishesDeletedAndRestoredTargetsAfterCommittedEdits()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var histories = new EditorHistories(); var documents = new EditorDocumentService();
            documents.Load(new() { Document = new() { RootNodeId = "g", Nodes = [new() { Id = "g", Type = "Group" }] }, GroupEntries = { ["g"] = [] } });
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            var fields = new EntryFieldAuthoringService(documents, histories);
            using var projections = new EditorGroupProjectionStore(documents, catalog, fields);
            using var query = new ProjectSceneTargets(documents, histories: histories);
            var batches = new GroupEntryAuthoringService(documents, catalog, histories);
            var creatorId = batches.Insert("g", 0, 1, BuiltinEntryIds.ShowLayer).Single();
            var creator = projections.Get("g").Entries.Single(entry => entry.StableId == creatorId);
            var consumerId = batches.Insert("g", 1, 1, BuiltinEntryIds.HideLayer).Single();
            var consumer = projections.Get("g").Entries.Single(entry => entry.StableId == consumerId);
            Dispatcher.UIThread.RunJobs();
            var counts = new List<int>();
            query.Changed += () => counts.Add(query.Before("g", consumer.StableId).Targets.Count);
            batches.Delete("g", [creator.StableId]); Dispatcher.UIThread.RunJobs();
            await histories.Content.UndoAsync(); Dispatcher.UIThread.RunJobs();
            histories.Content.MarkSaved(); Dispatcher.UIThread.RunJobs();
            Assert.That(counts, Is.EqualTo(new[] { 0, 1 }), "Content commits notify without requiring a resource refresh; saving does not masquerade as an edit.");
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryChangesPublishOnceWithFinalAuthorStateAndNewHistoryCursor()
    {
        using var histories = new EditorHistories(); var documents = new EditorDocumentService();
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.value", new DynamicParameterTable([]))])]);
        documents.Load(new() { Document = new() { Nodes = [new() { Id = "g", Type = "Group", File = "groups/g.rawgalgroup" }] }, GroupEntries = { ["g"] = [] } });
        var fields = new EntryFieldAuthoringService(documents, histories);
        using var projections = new EditorGroupProjectionStore(documents, catalog, fields);
        var group = projections.Get("g"); var batches = new GroupEntryAuthoringService(documents, catalog, histories);
        var observed = new List<(AuthorChangeSet Change, int Count, bool Busy, bool Undo, bool Redo)>();
        histories.Content.AuthorChanges.Committed += change => observed.Add((change, documents.Groups["g"].Entries.Count, histories.Content.IsBusy, histories.Content.CanUndo, histories.Content.CanRedo));
        var addedIds = batches.Insert("g", 0, 3, "custom.value");
        var added = group.Entries.Where(entry => addedIds.Contains(entry.StableId)).ToArray();
        Assert.That(added, Has.Length.EqualTo(3)); Assert.That(group.Entries.Select(entry => entry.Source), Is.EqualTo(documents.Groups["g"].Entries));
        fields.SetCondition("g", added[1].Source, "true");
        fields.SetCondition("g", added[1].Source, "true"); // No-op is not a transaction.
        histories.Content.MarkSaved(); histories.Clear();
        await histories.Content.UndoAsync(); await histories.Content.RedoAsync();
        Assert.That(observed, Has.Count.EqualTo(2), "History status and empty Undo/Redo do not publish content changes.");
        batches.Delete("g", [added[0].StableId, added[2].StableId]);
        await histories.Content.UndoAsync(); await histories.Content.RedoAsync();
        Assert.That(observed.Select(item => item.Change.Revision), Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
        Assert.That(observed.Select(item => item.Change.Cause), Is.EqualTo(new[] { AuthorChangeCause.Execute, AuthorChangeCause.Execute, AuthorChangeCause.Execute, AuthorChangeCause.Undo, AuthorChangeCause.Redo }));
        Assert.That(observed.Select(item => item.Count), Is.EqualTo(new[] { 3, 3, 1, 3, 1 }));
        Assert.That(observed.All(item => !item.Busy), Is.True); Assert.That(observed[3].Redo, Is.True); Assert.That(observed[4].Undo, Is.True);
        Assert.That(observed.All(item => !item.Change.UnknownScope), Is.True);
        Assert.That(observed[0].Change.Groups.Single().Kind, Is.EqualTo(GroupChangeKind.Membership));
        Assert.That(observed[1].Change.Groups.Single().Kind, Is.EqualTo(GroupChangeKind.Condition));
        Assert.That(observed[1].Change.Groups.Single().FirstIndex, Is.EqualTo(1));
        Assert.That(observed[2].Change.Groups.Single().EntryIds, Is.EquivalentTo(new[] { added[0].StableId, added[2].StableId }));
    }

    [Test]
    public async Task CompositeFailureDiscardsAllRecordedChangesButKeepsCompensationRules()
    {
        using var history = new UndoRedoHistory(); var value = 0; var notifications = 0;
        history.AuthorChanges.Committed += _ => notifications++;
        var change = new RecordingEdit(history, () => value = 1, () => value = 0);
        var failure = new DelegateEdit("fail", () => { }, () => throw new IOException("domain write failed"));
        Assert.Throws<IOException>(() => history.Execute(new CompositeEdit("atomic", [change, failure])));
        Assert.That(value, Is.Zero); Assert.That(history.CanUndo, Is.False); Assert.That(history.AuthorChanges.Revision, Is.Zero); Assert.That(notifications, Is.Zero);
        Assert.ThrowsAsync<IOException>(async () => await history.ExecuteAsync(new AsyncCompositeEdit("atomic", [new AsyncEditAdapter(change), new AsyncEditAdapter(failure)])));
        Assert.That(value, Is.Zero); Assert.That(history.AuthorChanges.Revision, Is.Zero);
        history.Execute(change); Assert.That(notifications, Is.EqualTo(1));
        await history.UndoAsync(); Assert.That(value, Is.Zero); Assert.That(notifications, Is.EqualTo(2));
        var broken = new DelegateEdit("broken compensation", () => throw new IOException("rollback failed"), () => value = 9);
        Assert.Throws<HistoryRepairRequiredException>(() => history.Execute(new CompositeEdit("broken", [broken, failure])));
        Assert.That(history.RepairRequired, Is.True); Assert.That(notifications, Is.EqualTo(2));
    }

    [Test]
    public async Task MixedLegacyCompositesStayConservativeAndMergeScopedRanges()
    {
        using var history = new UndoRedoHistory(); var changes = new List<AuthorChangeSet>(); history.AuthorChanges.Committed += changes.Add;
        var first = new RecordingEdit(history, () => { }, () => { }, "g", 6);
        var second = new RecordingEdit(history, () => { }, () => { }, "g", 2);
        await history.ExecuteAsync(new AsyncCompositeEdit("scoped", [new AsyncEditAdapter(first), new AsyncEditAdapter(second)]));
        Assert.That(changes.Single().Groups.Single().FirstIndex, Is.EqualTo(2)); Assert.That(changes.Single().UnknownScope, Is.False);
        history.Execute(new CompositeEdit("mixed", [first, new DelegateEdit("legacy", () => { }, () => { })]));
        Assert.That(changes.Last().UnknownScope, Is.True); Assert.That(changes.Last().Groups, Has.Count.EqualTo(1));
        await history.UndoAsync(); Assert.That(changes.Last().UnknownScope, Is.True);
    }

    [Test]
    public async Task CommitObserversCannotReenterOrSuppressOtherObservers()
    {
        using var history = new UndoRedoHistory(); var count = 0; var callbacks = 0;
        Exception? syncFailure = null, asyncFailure = null;
        history.AuthorChanges.Committed += _ =>
        {
            try { history.Execute(new DelegateEdit("reenter", () => { }, () => count++)); } catch (Exception error) { syncFailure = error; }
            try { history.ExecuteAsync(new DelegateEdit("reenter async", () => { }, () => count++)).GetAwaiter().GetResult(); } catch (Exception error) { asyncFailure = error; }
            throw new IOException("view failed");
        };
        history.AuthorChanges.Committed += _ => callbacks++;
        await history.ExecuteAsync(new DelegateEdit("author", () => count--, () => count++));
        Assert.That(count, Is.EqualTo(1)); Assert.That(callbacks, Is.EqualTo(1)); Assert.That(history.CanUndo, Is.True);
        Assert.That(syncFailure, Is.TypeOf<InvalidOperationException>()); Assert.That(asyncFailure, Is.TypeOf<InvalidOperationException>());
        Assert.That(history.AuthorChanges.LastNotificationError, Is.TypeOf<IOException>());
    }

    [Test]
    public void FieldPatchesReportParametersAndTypeSeparately()
    {
        using var histories = new EditorHistories(); var documents = new EditorDocumentService();
        var entry = new EditorEntryData { StableId = "same", Type = "custom.old" };
        documents.Load(new()
        {
            Document = new() { Nodes = [new() { Id = "a", Type = "Group" }, new() { Id = "b", Type = "Group" }] },
            GroupEntries = { ["a"] = [entry], ["b"] = [new() { StableId = "same", Type = "custom.old" }] }
        });
        var author = new EntryFieldAuthoringService(documents, histories); var changes = new List<AuthorChangeSet>(); histories.Content.AuthorChanges.Committed += changes.Add;
        author.SetParameters("a", entry, entry.Type, new() { ["future"] = JsonSerializer.SerializeToElement(new { opaque = new[] { 1, 2 } }) });
        author.SetType("a", entry, "custom.new", new());
        Assert.That(changes[0].Groups.Single().Kind, Is.EqualTo(GroupChangeKind.Parameters));
        Assert.That(changes[1].Groups.Single().Kind, Is.EqualTo(GroupChangeKind.EntryType | GroupChangeKind.Parameters));
        Assert.That(changes.SelectMany(change => change.Groups).All(group => group.GroupId == "a"), Is.True);
        Assert.That(documents.Groups["b"].Entries.Single().Type, Is.EqualTo("custom.old"));
    }

    private sealed class RecordingEdit(UndoRedoHistory history, Action redo, Action undo, string group = "g", int first = 0) : IUndoableEdit, IAuthorScopedEdit
    {
        public string Description => "record";
        public void Redo() { redo(); Record(); }
        public void Undo() { undo(); Record(); }
        private void Record() => history.AuthorChanges.Record(group, first, GroupChangeKind.Parameters, ["entry"]);
    }
}
