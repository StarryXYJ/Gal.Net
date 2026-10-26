using GalNet.Contracts.Builtins.Gallery;
using GalNet.Contracts.Builtins.Assets;
using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task DirtyGroupFileRenameSaveUndoKeepsNewlySavedTextAndDocumentIdentity()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "FileRename"), "Files", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var document = documents.Groups[group.Id];
        var originalPath = document.RelativePath;
        var original = File.ReadAllBytes(Path.Combine(project.RootPath, originalPath));
        var entry = workspace.InsertEntriesInto(group, 0, 1).Single();
        entry.Type = "dialogue.text"; entry.Parameters = new() { ["content"] = "unsaved dialogue" };
        await files.ExecuteAsync(new(ProjectFileAction.Move, [originalPath], "Graph/groups/renamed.rawgalgroup", false));
        Assert.That(documents.Groups[group.Id], Is.SameAs(document));
        Assert.That(document.RelativePath, Is.EqualTo("Graph/groups/renamed.rawgalgroup"));
        Assert.That(document.Entries.Single(), Is.SameAs(entry.Source));
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, document.RelativePath)), Is.EqualTo(original));
        Assert.That(content.IsDirty, Is.True);
        await workspace.SaveAsync();
        var saved = File.ReadAllBytes(Path.Combine(project.RootPath, document.RelativePath));
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(document.RelativePath, Is.EqualTo(originalPath));
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, originalPath)), Is.EqualTo(saved));
        Assert.That(content.IsDirty, Is.False);
        Assert.That(entry.Source.Parameters["content"].GetString(), Is.EqualTo("unsaved dialogue"));
        await workspace.UndoRedoHistory.RedoAsync();
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["Graph/groups", document.RelativePath], "Graph/chapters", false));
        Assert.That(document.RelativePath, Is.EqualTo("Graph/chapters/renamed.rawgalgroup"));
        Assert.That(Directory.Exists(Path.Combine(project.RootPath, "Graph/groups")), Is.False);
        await workspace.UndoRedoHistory.UndoAsync();
        Assert.That(document.RelativePath, Is.EqualTo("Graph/groups/renamed.rawgalgroup"));
        await projects.CloseAsync();
    }

    [Test]
    public async Task ImportRedoUsesRetainedBytesAndDirectoryMovePreservesMetadataAndEmptyFolders()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Assets"), "Assets", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var source = Path.Combine(_root, "picture.png"); File.WriteAllBytes(source, [1, 2, 3]);
        File.WriteAllText(source + ".meta", """{"id":"original","type":"sprite","path":"old.png","gallery":[{"id":17,"typeId":"cg"}],"custom":{"x":17}}""");
        await files.ExecuteAsync(new(ProjectFileAction.Import, [source], "Assets"));
        var codec = new AssetMetaCodec(BuiltinResourceTypes.CreateCatalog());
        var imported = codec.Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/picture.png.meta")));
        Assert.That(imported.Id, Is.Not.EqualTo("original"));
        File.Delete(source); File.Delete(source + ".meta");
        await history.UndoAsync();
        Assert.That(File.Exists(Path.Combine(project.RootPath, "Assets/picture.png")), Is.False);
        await history.RedoAsync();
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "Assets/picture.png")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        await files.ExecuteAsync(new(ProjectFileAction.CreateDirectory, [], "Assets/folder/empty"));
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["Assets/picture.png"], "Assets/folder"));
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["Assets/folder", "Assets/folder/picture.png"], "Assets/renamed", false));
        var moved = codec.Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/renamed/picture.png.meta")));
        Assert.That(moved.Id, Is.EqualTo(imported.Id));
        Assert.That(moved.Path, Is.EqualTo("renamed/picture.png"));
        Assert.That(moved.ExtensionData!["custom"].GetProperty("x").GetInt32(), Is.EqualTo(17));
        Assert.That(moved.ReadGallery(), Has.Count.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(project.RootPath, "Assets/renamed/empty")), Is.True);
        await history.UndoAsync();
        Assert.That(Directory.Exists(Path.Combine(project.RootPath, "Assets/folder/empty")), Is.True);
        await files.ExecuteAsync(new(ProjectFileAction.Copy, ["Assets/folder"], "Assets/copied", false));
        var copy = codec.Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/copied/picture.png.meta")));
        Assert.That(copy.Id, Is.Not.EqualTo(imported.Id));
        Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
        await projects.CloseAsync();
    }

    [TestCase("../outside")]
    [TestCase("Contracts/test.dll")]
    [TestCase("Graph/graph.json")]
    [TestCase(".galnet/test.txt")]
    [TestCase("I18n/zh-CN")]
    [TestCase("I18n/catalog/characters.json")]
    public async Task ProtectedAndInvalidFilePathsFailWithoutHistory(string path)
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Protected"), "Protected", new(), Contracts());
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        Exception? failure = null;
        try { await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], path)); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<EditorOperationException>());
        Assert.That(history.CanUndo, Is.False);
        await projects.CloseAsync();
    }

    [Test]
    public async Task CaseOnlyRenameChangesPhysicalSpellingAndRestoresIt()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows case alias path behavior");
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Case"), "Case", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/picture.png", Content: [1, 2]));
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["Assets/picture.png"], "Assets/PICTURE.png", false));
        Assert.That(Directory.GetFiles(Path.Combine(project.RootPath, "Assets")).Select(Path.GetFileName), Does.Contain("PICTURE.png"));
        await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
        Assert.That(Directory.GetFiles(Path.Combine(project.RootPath, "Assets")).Select(Path.GetFileName), Does.Contain("picture.png"));
        await projects.CloseAsync();
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task FilePublicationUndoAndRedoFailuresRestoreBytesFoldersAndHistory(int phase)
    {
        var fault = new PasteFaultTransaction();
        using var services = Services(transaction: fault);
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Fault"), "Fault", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/original/a.bin", Content: [1, 2, 3]));
        history.Clear();
        var move = new ProjectFileRequest(ProjectFileAction.Move, ["Assets/original"], "Assets/moved", false);
        if (phase > 0) await files.ExecuteAsync(move);
        if (phase == 2) await history.UndoAsync();
        var before = JsonSerializer.Serialize(content.CaptureWorking());
        var oldUndo = history.UndoDescription; var oldRedo = history.RedoDescription;
        var snapshot = Directory.GetFiles(project.RootPath, "*", SearchOption.AllDirectories).Where(path => !path.Contains(".galnet")).ToDictionary(path => path, path => GalNet.Editor.Shared.Commands.AuthorFileTransaction.ReadHash(path));
        fault.Armed = true;
        Exception? failure = null;
        try { if (phase == 0) await files.ExecuteAsync(move); else if (phase == 1) await history.UndoAsync(); else await history.RedoAsync(); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<IOException>());
        Assert.That(fault.Published, Is.EqualTo(1));
        Assert.That(JsonSerializer.Serialize(content.CaptureWorking()), Is.EqualTo(before));
        Assert.That(history.UndoDescription, Is.EqualTo(oldUndo)); Assert.That(history.RedoDescription, Is.EqualTo(oldRedo));
        Assert.That(Directory.GetFiles(project.RootPath, "*", SearchOption.AllDirectories).Where(path => !path.Contains(".galnet")), Is.EquivalentTo(snapshot.Keys));
        foreach (var pair in snapshot) Assert.That(GalNet.Editor.Shared.Commands.AuthorFileTransaction.ReadHash(pair.Key), Is.EqualTo(pair.Value));
        if (phase == 0) await files.ExecuteAsync(move); else if (phase == 1) await history.UndoAsync(); else await history.RedoAsync();
        await projects.CloseAsync();
    }

    [Test]
    public async Task FileDeleteChecksUnopenedGroupReferencesAndRestoresCompositeGroupOrder()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Delete"), "Delete", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var other = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 300, 300);
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/picture.png", Content: [1, 2]));
        var id = new AssetMetaCodec(BuiltinResourceTypes.CreateCatalog()).Deserialize(File.ReadAllText(Path.Combine(project.RootPath, "Assets/picture.png.meta"))).Id;
        var entry = workspace.InsertEntriesInto(other, 0, 1).Single(); entry.Type = "dialogue.text"; entry.Parameters = new() { ["content"] = "custom link " + id };
        await workspace.SaveAsync();
        await projects.CloseAsync(); project = await projects.OpenAsync(project.RootPath);
        files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        history = project.Services.GetRequiredService<EditorHistories>().Content;
        content = project.Services.GetRequiredService<IProjectContentSession>();
        Exception? failure = null;
        try { await files.ExecuteAsync(new(ProjectFileAction.Delete, ["Assets/picture.png"])); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<EditorOperationException>()); Assert.That(File.Exists(Path.Combine(project.RootPath, "Assets/picture.png")), Is.True);
        using (var inspect = new ProjectFileOperation(project.RootPath, new(ProjectFileAction.Delete, ["Assets/picture.png"]), project.Services.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>()))
        {
            var impact = inspect.Analyze(content.CaptureWorking(), content.CaptureSaved());
            Assert.That(impact.References.Single().Document, Does.Contain(other.Id)); Assert.That(impact.References.Single().Certain, Is.False);
        }
        await files.ExecuteAsync(new(ProjectFileAction.Delete, ["Assets/picture.png"], AllowCandidateReferences: true));
        await history.UndoAsync();
        var original = JsonSerializer.Serialize(content.CaptureWorking());
        var source = "Graph/" + content.CaptureWorking().Graph.Nodes.Single(node => node.Id == group.Id).File;
        await files.ExecuteAsync(new(ProjectFileAction.Delete, [source], DeleteGroups: true));
        Assert.That(File.Exists(Path.Combine(project.RootPath, source)), Is.False);
        Assert.That(content.CaptureWorking().Graph.Nodes.Any(node => node.Id == group.Id), Is.False);
        await history.UndoAsync(); Assert.That(JsonSerializer.Serialize(content.CaptureWorking()), Is.EqualTo(original));
        await projects.CloseAsync();
    }

    [Test]
    public async Task FileConflictsAndI18nCrossCultureMovesAreRejectedBeforePublication()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Conflict"), "Conflict", new(), Contracts());
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "notes/a.txt", Content: [1]));
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["notes/a.txt"], "notes/b.txt", false));
        await history.UndoAsync(); File.WriteAllBytes(Path.Combine(project.RootPath, "notes/b.txt"), [9]);
        Assert.ThrowsAsync<EditorOperationException>(() => history.RedoAsync());
        Assert.That(File.ReadAllBytes(Path.Combine(project.RootPath, "notes/b.txt")), Is.EqualTo(new byte[] { 9 }));
        Assert.That(File.Exists(Path.Combine(project.RootPath, "notes/a.txt")), Is.True);
        Directory.CreateDirectory(Path.Combine(project.RootPath, "I18n/zh-CN")); Directory.CreateDirectory(Path.Combine(project.RootPath, "I18n/en-US"));
        File.WriteAllText(Path.Combine(project.RootPath, "I18n/zh-CN/a.json"), "{}");
        Exception? failure = null;
        try { await files.ExecuteAsync(new(ProjectFileAction.Move, ["I18n/zh-CN/a.json"], "I18n/en-US")); } catch (Exception error) { failure = error; }
        Assert.That(failure, Is.TypeOf<EditorOperationException>()); Assert.That(File.Exists(Path.Combine(project.RootPath, "I18n/zh-CN/a.json")), Is.True);
        await projects.CloseAsync();
    }

    [Test]
    public async Task LargeOrdinaryFileMovesUseRetainedDiskCopiesAndHistoryReleasesThem()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Large"), "Large", new(), Contracts());
        Directory.CreateDirectory(Path.Combine(project.RootPath, "notes"));
        using (var stream = File.Create(Path.Combine(project.RootPath, "notes/large.bin"))) stream.SetLength(24 * 1024 * 1024);
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var allocated = GC.GetTotalAllocatedBytes();
        await files.ExecuteAsync(new(ProjectFileAction.Move, ["notes/large.bin"], "notes/moved.bin", false));
        Assert.That(GC.GetTotalAllocatedBytes() - allocated, Is.LessThan(12 * 1024 * 1024), "File bytes must not be materialized in managed buffers.");
        var retained = Path.Combine(project.RootPath, ".galnet/editor/history");
        Assert.That(Directory.GetDirectories(retained), Is.Not.Empty);
        await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
        project.Services.GetRequiredService<EditorHistories>().Content.Clear();
        Assert.That(Directory.GetDirectories(retained), Is.Empty);
        Assert.That(new FileInfo(Path.Combine(project.RootPath, "notes/large.bin")).Length, Is.EqualTo(24 * 1024 * 1024));
        await projects.CloseAsync();
    }
}
