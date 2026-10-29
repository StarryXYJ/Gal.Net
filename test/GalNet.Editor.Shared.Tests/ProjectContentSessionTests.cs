using System.Text;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed class ProjectContentSessionTests
{
    private string _root = null!;
    private EditorDocumentService _documents = null!;
    private TestRepository _repository = null!;
    private ProjectContentSession _session = null!;
    private GalProject _project = null!;

    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-session-" + Guid.NewGuid().ToString("N"));
        await EditorProjectCreator.CreateAsync(new(_root, "Original"));
        var settings = DirectProjectPersistence.LoadSettings(_root);
        _repository = new(new EditorDocumentRepository(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules())));
        _documents = new();
        _documents.Load(_repository.Load(_root));
        _project = new("project", "Original", _root, settings, new(), new EmptyScope());
        _session = new(_documents, _repository);
        _session.Open(_project);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _project.DisposeAsync();
        _session.Dispose();
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task DirtyComparisonDetectsDirectLateMutationsAndIgnoresDisplayNumbers()
    {
        var entries = _documents.LoadedProject.GroupEntries.Values.First();
        entries.Clear();
        entries.Add(new()
        {
            StableId = "line",
            Id = 1,
            Type = BuiltinEntryIds.Wait,
            Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1f) }
        });
        await _session.SaveAsync();
        entries[0].Id = 99;
        Assert.That(_session.IsDirty, Is.False);
        entries[0].Parameters["future"] = JsonSerializer.SerializeToElement(new { precise = 9007199254740993L });
        Assert.That(_session.IsDirty, Is.True, "A read cannot cache away a mutation outside history notifications.");
        entries[0].Parameters.Remove("future");
        Assert.That(_session.IsDirty, Is.False);
        entries[0].StableId = "replacement";
        Assert.That(_session.IsDirty, Is.True);
        entries[0].StableId = "line";
        Assert.That(_session.IsDirty, Is.False);
    }

    [Test]
    public async Task RepeatedDirtyReadsDoNotAllocateSerializedCopiesOfEveryEntry()
    {
        var entries = _documents.LoadedProject.GroupEntries.Values.First();
        entries.Clear();
        for (var index = 0; index < 500; index++) entries.Add(new()
        {
            StableId = "line-" + index,
            Type = BuiltinEntryIds.Wait,
            Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1f) }
        });
        await _session.SaveAsync();
        _ = _session.IsDirty;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var dirty = false;
        for (var index = 0; index < 20; index++) dirty |= _session.IsDirty;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(dirty, Is.False);
        Assert.That(allocated, Is.LessThan(2_000_000), "Status reads must not serialize and clone the complete project.");
    }

    [Test]
    public async Task StructurePreparationSeesTheBaselineOfAnEarlierSaveThatWasStillPublishing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        _repository.BeforeApply = () => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        _documents.CurrentDocument.Name = "Just saved";
        var saving = _session.SaveAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        string? baseline = null;
        var structure = _session.PublishStructureAsync((working, saved) =>
        {
            baseline = saved.Graph.Name;
            return new GroupPresence(true);
        });
        Assert.That(baseline, Is.Null, "Preparation must wait for the publication gate.");
        release.Set();
        await Task.WhenAll(saving, structure).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(baseline, Is.EqualTo("Just saved"));
        Assert.That(_session.CaptureSaved().Graph.Name, Is.EqualTo("Just saved"));
    }

    [Test]
    public async Task SaveAllIncludesGraphGroupsAndCompleteSettingsAndComparesActualContent()
    {
        _documents.CurrentDocument.Name = "Renamed";
        var options = _project.Configuration.ReadDefaultOptions();
        options.DefaultWidth = 1440;
        options.SaveSlotCount = 17;
        _project.Configuration.Set(DefaultProjectConfiguration.Section, options);
        Assert.That(_session.IsDirty, Is.True);
        await _session.SaveAsync();
        Assert.That(_session.IsDirty, Is.False);
        Assert.That(_repository.LastPlan!.Writes.Keys, Does.Contain("Graph/graph.json"));
        Assert.That(_repository.LastPlan.Writes.Keys.Any(path => path.EndsWith(".rawgalgroup", StringComparison.Ordinal)), Is.True);
        Assert.That(DirectProjectPersistence.LoadSettings(_root).ReadDefaultOptions().DefaultWidth, Is.EqualTo(1440));
        _documents.CurrentDocument.Name = "Other";
        Assert.That(_session.IsDirty, Is.True);
        _documents.CurrentDocument.Name = "Renamed";
        Assert.That(_session.IsDirty, Is.False, "Actual author content, rather than a history cursor, defines dirty.");
    }

    [Test]
    public async Task EditingDuringPublicationDoesNotMarkTheNewerContentSaved()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        _repository.BeforeApply = () => { entered.SetResult(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
        _documents.CurrentDocument.Name = "Captured";
        var save = _session.SaveAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(_session.IsPublishing, Is.True);
            Assert.That(_session.HasEditsSincePublication, Is.False);
            _documents.CurrentDocument.Name = "Newer";
            _session.Refresh();
            Assert.That(_session.HasEditsSincePublication, Is.True);
        }
        finally { release.Set(); }
        await save;
        Assert.That(_session.HasEditsSincePublication, Is.False);
        Assert.That(_session.IsDirty, Is.True);
        using var disk = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")));
        Assert.That(disk.RootElement.GetProperty("name").GetString(), Is.EqualTo("Captured"));
        _repository.BeforeApply = null;
        await _session.SaveAsync();
        Assert.That(_session.IsDirty, Is.False);
    }

    [Test]
    public async Task ExternalSettingsChangeRefusesTheWholePlan()
    {
        var graph = File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json"));
        _documents.CurrentDocument.Name = "Editor";
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"saveSlotCount\":42}");
        try { await _session.SaveAsync(); Assert.Fail("External settings must block Save All."); }
        catch (IOException) { }
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")), Is.EqualTo(graph));
        Assert.That(_session.IsDirty, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PublicationFailureRestoresAllOrBlocksWritesAndRetainsRecovery(bool failRecovery)
    {
        var original = File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json"));
        _documents.CurrentDocument.Name = "Unsaved";
        var options = _project.Configuration.ReadDefaultOptions();
        options.DefaultWidth = 1440;
        _project.Configuration.Set(DefaultProjectConfiguration.Section, options);
        _repository.Transaction = new FaultTransaction(failRecovery, 3);
        try { await _session.SaveAsync(); Assert.Fail("Injected publication must fail."); }
        catch (IOException) { }
        Assert.That(_session.IsDirty, Is.True);
        Assert.That(_session.RepairRequired, Is.EqualTo(failRecovery));
        Assert.That(Directory.GetFiles(Path.Combine(_root, ".galnet/editor/recovery"), "*.json"), Has.Length.EqualTo(1));
        if (failRecovery)
        {
            try { await _session.SaveAsync(); Assert.Fail("RepairRequired must block writes."); }
            catch (InvalidOperationException) { }
            Assert.That(Directory.GetDirectories(Path.Combine(_root, ".galnet/editor/transactions")), Has.Length.EqualTo(1));
            ProjectAuthorFileTransaction.Recover(_root);
        }
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")), Is.EqualTo(original));
        Assert.That(Directory.GetDirectories(Path.Combine(_root, ".galnet/editor/transactions")), Is.Empty);
        if (!failRecovery) { _repository.Transaction = null; await _session.SaveAsync(); Assert.That(_session.IsDirty, Is.False); }
    }

    [Test]
    public void CrashRecoveryPreservesExternalEditsAndReportsTheBackupDirectory()
    {
        var transaction = new FaultTransaction(true);
        var writes = new Dictionary<string, byte[]> { ["Graph/graph.json"] = Encoding.UTF8.GetBytes("new"), ["settings.json"] = Encoding.UTF8.GetBytes("new-settings") };
        Assert.Throws<ProjectRepairRequiredException>(() => transaction.Commit(_root, writes, []));
        File.WriteAllText(Path.Combine(_root, "Graph/graph.json"), "external");
        Assert.Throws<ProjectRepairRequiredException>(() => ProjectAuthorFileTransaction.Recover(_root));
        Assert.That(File.ReadAllText(Path.Combine(_root, "Graph/graph.json")), Is.EqualTo("external"));
    }

    [Test]
    public void RecoverySnapshotsValidatePayloadAndPruneWithoutDeletingTheNewest()
    {
        var store = new ProjectRecoveryStore(_root, new() { RecoveryCopies = 3 });
        string? latest = null;
        for (var index = 0; index < 5; index++) latest = store.Capture(new() { Graph = new() { Name = index.ToString(System.Globalization.CultureInfo.InvariantCulture) } });
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(latest!)!, "*.json"), Has.Length.EqualTo(3));
        Assert.That(ProjectRecoveryStore.Read(latest!).Graph.Name, Is.EqualTo("4"));
        using var raw = JsonDocument.Parse(File.ReadAllBytes(latest!));
        var corrupted = raw.RootElement.GetRawText().Replace(raw.RootElement.GetProperty("hash").GetString()!, "BAD", StringComparison.Ordinal);
        File.WriteAllText(latest!, corrupted);
        Assert.Throws<InvalidDataException>(() => ProjectRecoveryStore.Read(latest!));
        var tiny = new ProjectRecoveryStore(_root, new() { RetainedByteBudget = 1 });
        var kept = tiny.Capture(new() { Graph = new() { Name = "oversized newest" } });
        Assert.That(File.Exists(kept), Is.True);
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(kept!)!, "*.json"), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task StructuralChangesTransformWorkingAndSavedVersionsIndependently()
    {
        var group = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        var identity = _documents.Groups[group.Id];
        var oldFile = group.File!;
        _documents.CurrentDocument.Name = "unsaved name";
        await _session.PublishStructureAsync(new RenameSource(group.Id, "groups/renamed.rawgalgroup"));
        Assert.That(File.Exists(Path.Combine(_root, "Graph", oldFile)), Is.False);
        Assert.That(File.Exists(Path.Combine(_root, "Graph/groups/renamed.rawgalgroup")), Is.True);
        Assert.That(_documents.Groups[group.Id], Is.SameAs(identity));
        Assert.That(identity.RelativePath, Is.EqualTo("Graph/groups/renamed.rawgalgroup"));
        using (var saved = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json"))))
            Assert.That(saved.RootElement.GetProperty("name").GetString(), Is.EqualTo("Original"));
        Assert.That(_documents.CurrentDocument.Name, Is.EqualTo("unsaved name"));
        Assert.That(_session.IsDirty, Is.True);
        await _session.SaveAsync();
        await _session.PublishStructureAsync(new RenameSource(group.Id, oldFile));
        Assert.That(_session.IsDirty, Is.False);
        using var afterUndo = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")));
        Assert.That(afterUndo.RootElement.GetProperty("name").GetString(), Is.EqualTo("unsaved name"), "Undo of structure must preserve unrelated edits saved in the meantime.");
    }

    [Test]
    public async Task GroupCreationAndDeletionShareTheAuthorFilePlan()
    {
        await _session.PublishStructureAsync(new GroupPresence(true));
        var file = Path.Combine(_root, "Graph/groups/created.rawgalgroup");
        Assert.That(File.Exists(file), Is.True);
        Assert.That(_session.IsDirty, Is.False);
        var options = _project.Configuration.ReadDefaultOptions();
        options.DefaultWidth = 1440;
        _project.Configuration.Set(DefaultProjectConfiguration.Section, options);
        await _session.PublishStructureAsync(new GroupPresence(false));
        Assert.That(File.Exists(file), Is.False);
        Assert.That(_session.IsDirty, Is.True);
        Assert.That(DirectProjectPersistence.LoadSettings(_root).ReadDefaultOptions().DefaultWidth, Is.EqualTo(1920));
    }

    [Test]
    public async Task PeriodicRecoveryCapturesChangedAuthorContentWithoutSavingIt()
    {
        _session.Dispose();
        _session = new(_documents, _repository, new() { RecoveryInterval = TimeSpan.FromMilliseconds(15) });
        _session.Open(_project);
        _documents.CurrentDocument.Name = "periodic unsaved";
        var directory = Path.Combine(_root, ".galnet/editor/recovery");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!Directory.Exists(directory) || Directory.GetFiles(directory, "*.json").Length == 0)
            await Task.Delay(10, deadline.Token);
        var recovered = ProjectRecoveryStore.Read(Directory.GetFiles(directory, "*.json").Single());
        Assert.That(recovered.Graph.Name, Is.EqualTo("periodic unsaved"));
        using var saved = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")));
        Assert.That(saved.RootElement.GetProperty("name").GetString(), Is.EqualTo("Original"));
    }

    private sealed record RenameSource(string GroupId, string File) : IProjectStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version) => document.Graph.Nodes.Single(node => node.Id == GroupId).File = File;
    }

    [Test]
    public async Task StructuralPreflightIncludesUnsavedGroupsBeforeTouchingTheSavedBaseline()
    {
        var group = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        var oldFile = group.File!;
        _documents.CurrentDocument.Nodes.Add(new() { Id = "unsaved", Type = "Group", File = "groups/taken.rawgalgroup" });
        _documents.LoadedProject.GroupEntries.Add("unsaved", []);
        try
        {
            await _session.PublishStructureAsync(new RenameSource(group.Id, "groups/taken.rawgalgroup"));
            Assert.Fail("An unsaved Group owns the proposed path.");
        }
        catch (InvalidDataException) { }
        Assert.That(group.File, Is.EqualTo(oldFile));
        Assert.That(File.Exists(Path.Combine(_root, "Graph", oldFile)), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "Graph/groups/taken.rawgalgroup")), Is.False);
    }

    [Test]
    public async Task FailureBeforeInstallingWorkingStructureCompensatesThePublishedBaseline()
    {
        var group = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        var oldFile = group.File!;
        _documents.CurrentDocument.Name = "still unsaved";
        try
        {
            await _session.PublishStructureAsync(new RejectInstall(group.Id));
            Assert.Fail("Injected installation must fail.");
        }
        catch (IOException) { }
        Assert.That(group.File, Is.EqualTo(oldFile));
        Assert.That(File.Exists(Path.Combine(_root, "Graph", oldFile)), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "Graph/groups/rejected.rawgalgroup")), Is.False);
        Assert.That(_session.IsDirty, Is.True);
        Assert.That(_session.RepairRequired, Is.False);
        await _session.SaveAsync();
        Assert.That(_session.IsDirty, Is.False);
    }

    private sealed class RejectInstall(string groupId) : IProjectStructureChange
    {
        private int _applications;
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            if (++_applications == 3) throw new IOException("injected working install failure");
            document.Graph.Nodes.Single(node => node.Id == groupId).File = "groups/rejected.rawgalgroup";
        }
    }

    [Test]
    public async Task DirtyExcludesDisplayedLineNumbersAndGroupLookupInsertionOrder()
    {
        var group = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        var entries = _documents.LoadedProject.GroupEntries[group.Id];
        entries.Add(new() { StableId = "wait", Type = BuiltinEntryIds.Wait, Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1f) } });
        await _session.SaveAsync();
        await _session.PublishStructureAsync(new GroupPresence(true));
        entries.Single().Id = 700;
        _documents.LoadedProject.GroupEntries.Remove(group.Id);
        _documents.LoadedProject.GroupEntries.Add(group.Id, entries);
        Assert.That(_session.IsDirty, Is.False);
    }

    [Test]
    public async Task DeletedGroupRestoresItsUnsavedWorkingValuesSeparatelyFromItsSavedValues()
    {
        var group = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        _documents.LoadedProject.GroupEntries[group.Id].Add(new()
        {
            StableId = "wait",
            Type = BuiltinEntryIds.Wait,
            Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1f) }
        });
        await _session.SaveAsync();
        _documents.LoadedProject.GroupEntries[group.Id].Single().Parameters["duration"] = JsonSerializer.SerializeToElement(2f);
        var working = _session.CaptureWorking();
        var saved = _session.CaptureSaved();
        await _session.PublishStructureAsync(new RemoveOrRestoreGroup(group.Id, false, working, saved));
        Assert.That(File.Exists(Path.Combine(_root, "Graph", group.File!)), Is.False);
        await _session.PublishStructureAsync(new RemoveOrRestoreGroup(group.Id, true, working, saved));
        Assert.That(_documents.LoadedProject.GroupEntries[group.Id].Single().Parameters["duration"].GetSingle(), Is.EqualTo(2f));
        Assert.That(_session.CaptureSaved().GroupEntries[group.Id].Single().Parameters["duration"].GetSingle(), Is.EqualTo(1f));
        Assert.That(_session.IsDirty, Is.True);
        await _session.SaveAsync();
        Assert.That(_session.IsDirty, Is.False);
    }

    private sealed record RemoveOrRestoreGroup(string GroupId, bool Restore, EditorProjectDocument Working, EditorProjectDocument Saved) : IProjectStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            if (Restore)
            {
                var original = GalNet.Editor.Abstraction.Changes.EditorDocumentCloner.Clone(version == AuthorDocumentVersion.Working ? Working : Saved);
                document.Graph.Nodes.Add(original.Graph.Nodes.Single(node => node.Id == GroupId));
                document.Graph.Edges.AddRange(original.Graph.Edges.Where(edge => edge.FromNodeId == GroupId || edge.ToNodeId == GroupId));
                document.GroupEntries.Add(GroupId, original.GroupEntries[GroupId]);
            }
            else
            {
                document.Graph.Nodes.RemoveAll(node => node.Id == GroupId);
                document.Graph.Edges.RemoveAll(edge => edge.FromNodeId == GroupId || edge.ToNodeId == GroupId);
                document.GroupEntries.Remove(GroupId);
            }
        }
    }

    private sealed record GroupPresence(bool Present) : IProjectStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            if (Present)
            {
                document.Graph.Nodes.Add(new() { Id = "created", Type = "Group", Name = "Created", File = "groups/created.rawgalgroup" });
                document.GroupEntries.Add("created", []);
            }
            else
            {
                document.Graph.Nodes.RemoveAll(node => node.Id == "created");
                document.GroupEntries.Remove("created");
            }
        }
    }

    private sealed class FaultTransaction(bool failRecovery, int failAt = 2) : AuthorFileTransaction
    {
        private int _published;
        protected override void Publish(string path, string? stagedFile)
        {
            if (++_published == failAt) throw new IOException("injected publication failure");
            base.Publish(path, stagedFile);
        }
        protected override void Restore(string path, string? backupFile)
        {
            if (failRecovery) throw new IOException("injected compensation failure");
            base.Restore(path, backupFile);
        }
    }

    private sealed class EmptyScope : IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => null;
        public void Dispose() { }
    }

    private sealed class TestRepository(IEditorDocumentRepository inner) : IEditorDocumentRepository
    {
        public Action? BeforeApply { get; set; }
        public AuthorFileTransaction? Transaction { get; set; }
        public ProjectSavePlan? LastPlan { get; private set; }
        public LoadedEditorProjectDocument Load(string path) => inner.Load(path);
        public ProjectSavePlan PrepareSave(string path, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> entries) => inner.PrepareSave(path, document, entries);
        public void ApplySave(string path, ProjectSavePlan plan, CancellationToken cancellationToken = default)
        {
            LastPlan = plan;
            BeforeApply?.Invoke();
            if (Transaction is not null) Transaction.Commit(path, plan.Writes, plan.DeletedFiles, cancellationToken);
            else inner.ApplySave(path, plan, cancellationToken);
        }
        public void Save(string path, EditorGraphDocument document, IReadOnlyDictionary<string, IReadOnlyList<EditorEntryData>> entries) => inner.Save(path, document, entries);
    }
}
