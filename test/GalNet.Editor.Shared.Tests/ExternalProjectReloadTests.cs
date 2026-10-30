using System.Text;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed class ExternalProjectReloadTests
{
    private string _root = null!;
    private IEntryCatalog _catalog = null!;
    private EditorDocumentRepository _repository = null!;
    private EditorDocumentService _documents = null!;
    private ProjectContentSession _content = null!;
    private GalProject _project = null!;

    [SetUp]
    public async Task SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "GalNet-external-" + Guid.NewGuid().ToString("N"));
        await EditorProjectCreator.CreateAsync(new(_root, "Original"));
        _catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        _repository = new(_catalog, EditorDocumentValidation.Authoring);
        var settings = DirectProjectPersistence.LoadSettings(_root);
        _documents = new(); _documents.Load(_repository.Load(_root));
        _project = new("project", "Original", _root, settings, new(), new EmptyScope());
        _content = new(_documents, _repository); _content.Open(_project);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _project.DisposeAsync(); _content.Dispose(); Directory.Delete(_root, true);
    }

    private ExternalProjectSnapshot Read() => ExternalProjectSnapshotReader.Read(_root, _content.FileDocuments.Values, _catalog);

    [Test]
    public async Task CliChangesReloadInTheSameSessionAndSubsequentSaveUsesTheNewDiskBaseline()
    {
        var group = _documents.Groups.Values.Single();
        var source = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        var result = await new ProjectEditService(_root, _catalog).ExecuteAsync([new RenameProjectCommand("External")]);
        Assert.That(result.Success, Is.True);
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Reloaded));
        Assert.That(_project.Name, Is.EqualTo("External"));
        Assert.That(_documents.Groups[group.Id], Is.SameAs(group));
        Assert.That(_documents.CurrentDocument.Nodes.Single(node => node.Id == source.Id), Is.SameAs(source));
        Assert.That(_content.IsDirty, Is.False);
        _documents.CurrentDocument.Name = "Editor after external";
        await _content.SaveAsync();
        Assert.That(_content.IsDirty, Is.False);
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Unchanged));
    }

    [Test]
    public async Task OwnPublicationDoesNotBecomeAnExternalReload()
    {
        var count = 0; _content.ExternalReloaded += () => count++;
        _documents.CurrentDocument.Name = "Local";
        await _content.SaveAsync();
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Unchanged));
        Assert.That(count, Is.Zero);
    }

    [TestCase("notes.txt", "before", "saved")]
    [TestCase("I18n/zh-CN/local.json", "{\"line\":\"before\"}", "{\"line\":\"saved\"}")]
    public async Task SavedManagedFileBaselineIsIndependentOfLaterAuthorEdits(string path, string before, string saved)
    {
        var fullPath = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, before);
        var file = _content.TrackFile(path, File.ReadAllBytes(fullPath));
        file.Data = Encoding.UTF8.GetBytes(saved);
        await _content.SaveAsync();
        file.Data[0] ^= 1;

        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Unchanged));
        Assert.That(_content.IsDirty, Is.True);
        Assert.That(File.ReadAllText(fullPath), Is.EqualTo(saved));
        Assert.That(Encoding.UTF8.GetString(_content.CaptureSaved().Files[file.Id].Data), Is.EqualTo(saved));
    }

    [Test]
    public async Task AcceptedExternalSnapshotDoesNotShareItsBaselineBytesWithTheCaller()
    {
        var path = Path.Combine(_root, "notes.txt");
        File.WriteAllText(path, "before");
        var file = _content.TrackFile("notes.txt", File.ReadAllBytes(path));
        File.WriteAllText(path, "external");
        File.AppendAllText(Path.Combine(_root, "settings.json"), "\n");
        var candidate = Read();
        Assert.That(await _content.ReloadExternalAsync(candidate), Is.EqualTo(ExternalReloadResult.Reloaded));
        candidate.Files["notes.txt"][0] ^= 1;
        candidate.Files["settings.json"][0] ^= 1;

        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Unchanged));
        Assert.That(Encoding.UTF8.GetString(file.Data), Is.EqualTo("external"));
        Assert.That(_content.IsDirty, Is.False);
        await _content.SaveAsync();
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Unchanged));
    }

    [Test]
    public async Task RejectedSaveDoesNotAcceptExternalSettingsAsItsBaseline()
    {
        var graph = File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json"));
        _documents.CurrentDocument.Name = "Unsaved";
        File.AppendAllText(Path.Combine(_root, "settings.json"), "\n");
        var error = Assert.ThrowsAsync<EditorOperationException>(() => _content.SaveAsync());
        Assert.That(error!.Code, Is.EqualTo("project.externalSettings"));
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json")), Is.EqualTo(graph));
        Assert.That(_content.CaptureSaved().Graph.Name, Is.EqualTo("Original"));

        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.LocalChanges));
        _documents.CurrentDocument.Name = "Original";
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Reloaded));
        await _content.SaveAsync();
        Assert.That(_content.IsDirty, Is.False);
    }

    [Test]
    public async Task LocalizationFilePublicationUndoAndRedoDoNotBecomeExternalReloads()
    {
        var count = 0;
        _content.ExternalReloaded += () => count++;
        _documents.CurrentDocument.Name = "Keep unsaved";
        const string path = "I18n/zh-CN/local.json";
        using var operation = new ProjectFileOperation(_root,
            new(ProjectFileAction.CreateFile, [], path, Content: Encoding.UTF8.GetBytes("{\"line\":\"local\"}")), _catalog);
        foreach (var forward in new[] { true, false, true })
        {
            await operation.ApplyAsync(forward, _content.PublishStructureAsync);
            Assert.That(File.Exists(Path.Combine(_root, path)), Is.EqualTo(forward));
            Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Unchanged));
            Assert.That(_content.IsDirty, Is.True);
            Assert.That(_content.CaptureSaved().Graph.Name, Is.EqualTo("Original"));
        }
        Assert.That(count, Is.Zero);
    }

    [Test]
    public async Task LocalChangesAndPendingInputArePreservedUntilExplicitDiscard()
    {
        _documents.CurrentDocument.Name = "Unsaved";
        var pending = true;
        using var draft = _content.RegisterDraft(() => pending, () => Task.CompletedTask, () => pending = false);
        await new ProjectEditService(_root, _catalog).ExecuteAsync([new RenameProjectCommand("External")]);
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.LocalChanges));
        Assert.That(_documents.CurrentDocument.Name, Is.EqualTo("Unsaved")); Assert.That(pending, Is.True);
        Assert.That(await _content.ReloadExternalAsync(Read(), true), Is.EqualTo(ExternalReloadResult.Reloaded));
        Assert.That(_documents.CurrentDocument.Name, Is.EqualTo("External")); Assert.That(pending, Is.False);
        Assert.That(_content.IsDirty, Is.False);
        Assert.That(Directory.GetFiles(Path.Combine(_root, ".galnet/editor/recovery")), Is.Not.Empty);
    }

    [Test]
    public async Task InvalidAndStaleCandidatesNeverReplaceTheLiveDocument()
    {
        var candidate = Read();
        var file = Path.Combine(_root, "Graph/graph.json"); var original = File.ReadAllBytes(file);
        File.WriteAllText(file, "{");
        Assert.That(await _content.ReloadExternalAsync(candidate), Is.EqualTo(ExternalReloadResult.Retry));
        Assert.Throws<InvalidDataException>(() => Read());
        Assert.That(_documents.CurrentDocument.Name, Is.EqualTo("Original"));
        File.WriteAllBytes(file, original);
        await new ProjectEditService(_root, _catalog).ExecuteAsync([new RenameProjectCommand("Recovered")]);
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Reloaded));
    }

    [Test]
    public void CandidateWaitsForTheWholeExternalTransaction()
    {
        var directory = Path.Combine(_root, ".galnet/editor/transactions/active"); Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, "manifest.json");
        File.WriteAllText(manifest, "{\"status\":\"publishing\"}");
        Assert.Throws<IOException>(() => Read());
        File.WriteAllText(manifest, "{\"status\":\"committed\"}");
        Assert.DoesNotThrow(() => Read());
    }

    [Test]
    public async Task ManagedTextAndLocalizationKeepIdentityAndAdoptDeletionAndNewFiles()
    {
        var textPath = Path.Combine(_root, "notes.txt"); File.WriteAllText(textPath, "before");
        var text = _content.TrackFile("notes.txt", File.ReadAllBytes(textPath));
        File.WriteAllText(textPath, "after");
        Directory.CreateDirectory(Path.Combine(_root, "I18n/zh-CN"));
        File.WriteAllText(Path.Combine(_root, "I18n/zh-CN/external.json"), "{\"line\":\"外部对白\"}");
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Reloaded));
        Assert.That(_content.FileDocuments[text.Id], Is.SameAs(text));
        Assert.That(Encoding.UTF8.GetString(text.Data), Is.EqualTo("after"));
        Assert.That(_content.FileDocuments.Values.Any(file => file.Path == "I18n/zh-CN/external.json" && file.ManagedContent), Is.True);
        File.Delete(textPath);
        Assert.That(await _content.ReloadExternalAsync(Read()), Is.EqualTo(ExternalReloadResult.Reloaded));
        Assert.That(text.Exists, Is.False); Assert.That(_content.IsDirty, Is.False);
        await _content.SaveAsync(); Assert.That(File.Exists(textPath), Is.False);
    }

    [Test]
    public async Task GroupDeletionAndRestorationDoNotLeaveAProjectionOnAnObsoleteDocument()
    {
        var group = _documents.Groups.Values.Single();
        var graph = File.ReadAllBytes(Path.Combine(_root, "Graph/graph.json"));
        var path = Path.Combine(_root, group.RelativePath); var bytes = File.ReadAllBytes(path);
        var removed = await new ProjectEditService(_root, _catalog).ExecuteAsync([new DeleteNodeCommand(group.Id)]);
        Assert.That(removed.Success, Is.True);
        await _content.ReloadExternalAsync(Read());
        File.WriteAllBytes(path, bytes); File.WriteAllBytes(Path.Combine(_root, "Graph/graph.json"), graph);
        await _content.ReloadExternalAsync(Read());
        Assert.That(_documents.GetOrCreateGroup(group.Id, group.RelativePath), Is.SameAs(group));
        Assert.That(_documents.LoadedProject.GroupEntries[group.Id], Is.SameAs(group.Entries));
    }

    [Test]
    public void ContractChangesAreReportedBeforeAnyLiveMutation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Contracts")); File.WriteAllText(Path.Combine(_root, "Contracts/new.dll"), "new contract");
        var candidate = Read();
        var error = Assert.ThrowsAsync<EditorOperationException>(async () => await _content.ReloadExternalAsync(candidate));
        Assert.That(error!.Code, Is.EqualTo("project.externalContracts")); Assert.That(_project.Name, Is.EqualTo("Original"));
    }

    private sealed class EmptyScope : IServiceScope
    {
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public void Dispose() { }
    }
}
