using System.Text.Json;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task ResourceNamesAndUnlockIdsShareUndoSaveAndTranslationProjection(bool group) => WithLocalizationProject(async project =>
    {
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var owner = documents.CurrentDocument.Nodes.First(node => node.Type == "Group").Id;
        if (!group)
        {
            owner = "Assets/note.dat";
            File.WriteAllText(Path.Combine(project.RootPath, owner), "data");
            File.WriteAllText(Path.Combine(project.RootPath, owner + ".meta"), """{"id":"a9000000000000000000000000000001","type":"data","path":"note.dat","custom":true}""");
        }
        var service = project.Services.GetRequiredService<ResourceMetadataAuthoringService>();
        var before = service.Capture(owner, group);
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await service.SetAsync(before, "资源名称", 51);
        var named = service.Capture(owner, group);
        Assert.That(named.Name, Is.EqualTo("资源名称")); Assert.That(named.UnlockId, Is.EqualTo(51));
        var key = named.Metadata.GetProperty("displayNameKey").GetString()!;
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>();
        Assert.That(panel.Snapshot("resources").Rows.Any(row => row.Key == key), Is.True);
        await history.UndoAsync();
        Assert.That(service.Capture(owner, group).UnlockId, Is.Null);
        Assert.That(service.Capture(owner, group).Name, Is.Empty);
        await history.RedoAsync();
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
        Assert.That(service.Capture(owner, group).Name, Is.EqualTo("资源名称"));
        await service.SetAsync(service.Capture(owner, group), "改名", null);
        Assert.That(service.Capture(owner, group).Metadata.GetProperty("displayNameKey").GetString(), Is.EqualTo(key));
        Assert.That(service.Capture(owner, group).UnlockId, Is.Null);
    });

    [Test]
    public Task ResourceMetadataRejectsStaleDraftsAndDuplicateGroupIdsWithoutLosingNames() => WithLocalizationProject(async project =>
    {
        var service = project.Services.GetRequiredService<ResourceMetadataAuthoringService>();
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var owner = documents.CurrentDocument.Nodes.First(node => node.Type == "Group").Id;
        var before = service.Capture(owner, true);
        await service.SetAsync(before, "原名", 71);
        Assert.ThrowsAsync<GalNet.Editor.Abstraction.Services.EditorOperationException>(() => service.SetAsync(before, "过期", 72));
        documents.CurrentDocument.Nodes.Add(new() { Id = "other", Type = "Group", Metadata = new AssetMeta { GalleryUnlockId = 72 } });
        Assert.ThrowsAsync<InvalidDataException>(() => service.SetAsync(service.Capture(owner, true), "改名", 72));
        Assert.That(service.Capture(owner, true).Name, Is.EqualTo("原名"));
    });
    [Test]
    public Task ImportOptionsPreserveManagedResourceNamesAndUndoTogether() => WithLocalizationProject(async project =>
    {
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/named.dat"), "data");
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/named.dat.meta"), """{"id":"a9000000000000000000000000000003","type":"data","path":"named.dat","custom":true}""");
        var service = project.Services.GetRequiredService<ResourceMetadataAuthoringService>();
        var draft = service.Open("Assets/named.dat", false);
        draft.Name = "Name"; draft.UnlockId = "91";
        await service.SetImportOptionsAsync("Assets/named.dat", "bilinear", "gzip");
        var metadata = service.Capture("Assets/named.dat", false);
        Assert.That(metadata.Name, Is.EqualTo("Name"));
        Assert.That(metadata.UnlockId, Is.EqualTo(91));
        Assert.That(metadata.Metadata.GetProperty("compress").GetString(), Is.EqualTo("gzip"));
        Assert.That(metadata.Metadata.GetProperty("custom").GetBoolean(), Is.True);
        var assets = project.Services.GetRequiredService<GalNet.Editor.Services.Interfaces.IAssetCatalogService>();
        Assert.That(assets.GetEntry("named.dat")!.Compress, Is.EqualTo("gzip"));
        await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
        Assert.That(service.Capture("Assets/named.dat", false).Name, Is.EqualTo("Name"));
        Assert.That(service.Capture("Assets/named.dat", false).Metadata.TryGetProperty("compress", out _), Is.False);
        Assert.That(assets.GetEntry("named.dat")!.Compress, Is.Null);
    });

    [Test]
    public Task PendingMetadataSurvivesCopyMoveDeleteAndUndo() => WithLocalizationProject(async project =>
    {
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/source.dat"), "data");
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/source.dat.meta"), """{"id":"a9000000000000000000000000000004","type":"data","path":"source.dat"}""");
        var service = project.Services.GetRequiredService<ResourceMetadataAuthoringService>();
        var files = project.Services.GetRequiredService<ProjectFileAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        await service.SetAsync(service.Capture("Assets/source.dat", false), "Pending name", 81);
        await files.ExecuteAsync(new(GalNet.Editor.Shared.Services.ProjectFileAction.Copy, ["Assets/source.dat"], "Assets/copy.dat", false));
        var copied = service.Capture("Assets/copy.dat", false);
        Assert.That(copied.Name, Is.EqualTo("Pending name"));
        Assert.That(copied.UnlockId, Is.Not.Null.And.Not.EqualTo(81));
        Assert.That(copied.ResourceId, Is.Not.EqualTo(service.Capture("Assets/source.dat", false).ResourceId));
        await files.ExecuteAsync(new(GalNet.Editor.Shared.Services.ProjectFileAction.Move, ["Assets/source.dat"], "Assets/moved.dat", false));
        Assert.That(service.Capture("Assets/moved.dat", false).Metadata.GetProperty("path").GetString(), Is.EqualTo("moved.dat"));
        Assert.That(service.Capture("Assets/moved.dat", false).Name, Is.EqualTo("Pending name"));
        await files.ExecuteAsync(new(GalNet.Editor.Shared.Services.ProjectFileAction.Delete, ["Assets/moved.dat"]));
        await history.UndoAsync();
        Assert.That(service.Capture("Assets/moved.dat", false).UnlockId, Is.EqualTo(81));
        await history.UndoAsync();
        Assert.That(service.Capture("Assets/source.dat", false).Name, Is.EqualTo("Pending name"));
    });

    [Test]
    public Task RenamingCopiedResourcesDoesNotOverwriteTheOriginalName() => WithLocalizationProject(async project =>
    {
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/one.dat"), "one");
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/two.dat"), "two");
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/one.dat.meta"), """{"id":"a9000000000000000000000000000001","type":"data","path":"one.dat","displayNameKey":"shared.name"}""");
        File.WriteAllText(Path.Combine(project.RootPath, "Assets/two.dat.meta"), """{"id":"a9000000000000000000000000000002","type":"data","path":"two.dat","displayNameKey":"shared.name"}""");
        WriteTranslation(project, """{"shared.name":"Original"}""");
        var service = project.Services.GetRequiredService<ResourceMetadataAuthoringService>();
        await service.SetAsync(service.Capture("Assets/two.dat", false), "Copy", null);
        Assert.That(service.Capture("Assets/one.dat", false).Name, Is.EqualTo("Original"));
        Assert.That(service.Capture("Assets/two.dat", false).Name, Is.EqualTo("Copy"));
    });

}
