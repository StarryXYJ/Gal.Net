using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private async Task WithLocalizationProject(Func<GalProject, Task> action, GalNet.Editor.Shared.Commands.AuthorFileTransaction? transaction = null, GalNet.Core.Entry.IEntryCatalog? catalog = null)
    {
        using var services = Services(transaction: transaction, catalog: catalog);
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "Localization"), "Localization", new(), Contracts());
        try { await action(project); }
        finally { await projects.CloseAsync(); }
    }

    private static void WriteTranslation(GalProject project, string json, string path = "I18n/zh-CN/lines.json")
    {
        var full = Path.Combine(project.RootPath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
    }

    [Test]
    public Task LocalizationP2ExistingTextEditsUndoSaveReopenAndKeepOtherLanguages() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """{"nested":{"line":"旧"},"keep":"留"}""");
        WriteTranslation(project, """{"nested.line":"English"}""", "I18n/en-US/lines.json");
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var authoring = project.Services.GetRequiredService<LocalizationAuthoringService>();
        var session = project.Services.GetRequiredService<IProjectContentSession>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var target = store.CaptureText("nested.line");
        var path = Path.Combine(project.RootPath, target.File.Path);
        await authoring.SetTextAsync(target, "新");
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(path), target.Key), Is.EqualTo("旧"), "Value changes remain unsaved.");
        Assert.That(session.IsDirty, Is.True);
        await history.UndoAsync();
        Assert.That(store.CaptureText(target.Key).Text, Is.EqualTo("旧"));
        Assert.That(session.IsDirty, Is.False);
        await history.RedoAsync();
        await session.SaveAsync();
        Assert.That(session.IsDirty, Is.False);
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(path), target.Key), Is.EqualTo("新"));
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(path), "keep"), Is.EqualTo("留"));
        Assert.That(store.CaptureText(target.Key, "en-US").Text, Is.EqualTo("English"));
        await history.UndoAsync();
        Assert.That(session.IsDirty, Is.True);
        await session.SaveAsync();
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(path), target.Key), Is.EqualTo("旧"));
        var projects = project.Services.GetRequiredService<IProjectService>();
        await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText(target.Key).Text, Is.EqualTo("旧"));
        Assert.That(() => store.CaptureText(target.Key), Throws.TypeOf<ObjectDisposedException>());
    });

    [Test]
    public Task LocalizationP2CreatingFilesPublishesOnlySavedBaselineAndRebasesUndoRedo() => WithLocalizationProject(async project =>
    {
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var session = project.Services.GetRequiredService<IProjectContentSession>();
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var authoring = project.Services.GetRequiredService<LocalizationAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        documents.CurrentDocument.Name = "Unsaved unrelated name";
        var target = store.CaptureText("new.line");
        await authoring.SetTextAsync(target, "正文");
        var full = Path.Combine(project.RootPath, target.File.Path);
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(full), target.Key), Is.Null, "New file publication establishes only an empty saved baseline.");
        using (var graph = JsonDocument.Parse(File.ReadAllText(Path.Combine(project.RootPath, "Graph/graph.json"))))
            Assert.That(graph.RootElement.GetProperty("name").GetString(), Is.EqualTo("Localization"));
        await session.SaveAsync();
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(full), target.Key), Is.EqualTo("正文"));
        await history.UndoAsync();
        Assert.That(File.Exists(full), Is.False);
        Assert.That(documents.CurrentDocument.Name, Is.EqualTo("Unsaved unrelated name"));
        await history.RedoAsync();
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(full), target.Key), Is.EqualTo("正文"), "Redo restores the saved version captured at undo, not an obsolete empty baseline.");
        Assert.That(session.IsDirty, Is.False);
    });

    [Test]
    public Task LocalizationP2ReplacingAnUndoneNewFileAtTheSamePathCanBeSaved() => WithLocalizationProject(async project =>
    {
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var authoring = project.Services.GetRequiredService<LocalizationAuthoringService>();
        var session = project.Services.GetRequiredService<IProjectContentSession>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        await authoring.SetTextAsync(store.CaptureText("first"), "第一份");
        await history.UndoAsync();
        var replacement = store.CaptureText("replacement");
        await authoring.SetTextAsync(replacement, "第二份");
        await session.SaveAsync();
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(Path.Combine(project.RootPath, replacement.File.Path)), "replacement"), Is.EqualTo("第二份"));
        Assert.That(session.IsDirty, Is.False);
    });

    [Test]
    public Task LocalizationP2ValidRelatedDraftRebasesAndOtherDraftsStayPending() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """{"line":"原文","other":"旧"}""");
        WriteTranslation(project, "{}", "I18n/zh-CN/unrelated.json");
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var target = store.CaptureText("line");
        var texts = project.Services.GetRequiredService<ProjectTextFiles>();
        var buffer = texts.Open(target.File.Path);
        var unrelated = texts.Open("I18n/zh-CN/unrelated.json"); unrelated.Text = "{broken unrelated";
        buffer.Text = """{"line":"原文","other":"草稿"}""";
        await project.Services.GetRequiredService<LocalizationAuthoringService>().SetTextAsync(target, "更新");
        Assert.That(store.CaptureText("other").Text, Is.EqualTo("草稿"));
        Assert.That(buffer.Text, Does.Contain("更新"));
        Assert.That(unrelated.Text, Is.EqualTo("{broken unrelated"));
    });

    [Test]
    public Task LocalizationP2InvalidRelatedDraftIsPreservedAndDoesNotEnterHistory() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """{"line":"原文"}""");
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var target = store.CaptureText("line");
        var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open(target.File.Path);
        buffer.Text = "{broken";
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        try { await project.Services.GetRequiredService<LocalizationAuthoringService>().SetTextAsync(target, "不写入"); Assert.Fail("Expected invalid JSON rejection."); }
        catch (JsonException) { }
        Assert.That(buffer.Text, Is.EqualTo("{broken"));
        Assert.That(history.CanUndo, Is.False);
        Assert.That(store.CaptureText("line").Text, Is.EqualTo("原文"));
    });

    [Test]
    public Task LocalizationP2StaleTextDraftSurvivesDocumentChangeAndCannotOverwriteIt() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """{"line":"旧"}""");
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var target = store.CaptureText("line");
        var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open(target.File.Path);
        var operation = store.Prepare(store.PrepareText(target, "新"));
        buffer.Text = """{"line":"草稿"}""";
        await project.Services.GetRequiredService<LocalizationAuthoringService>().ApplyAsync(operation, target.File.Path);
        Assert.That(buffer.Text, Does.Contain("草稿"));
        Assert.That(buffer.Error, Is.Not.Empty);
        try { await buffer.CommitAsync(); Assert.Fail("Expected conflict."); } catch (EditorOperationException) { }
        Assert.That(store.CaptureText("line").Text, Is.EqualTo("新"));
        buffer.CancelCommand.Execute(null);
        Assert.That(buffer.Text, Does.Not.Contain("草稿"));
        Assert.That(buffer.Error, Is.Empty);
    });

    [Test]
    public async Task LocalizationP2MountedTextPageKeepsConflictDraftAndRefreshesAfterCancel()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            await WithLocalizationProject(async project =>
            {
                WriteTranslation(project, """{"line":"旧"}""");
                var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
                var target = store.CaptureText("line");
                var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open(target.File.Path);
                using var vm = new FileDocumentPanelViewModel(buffer);
                var view = new FileDocumentPanelView { DataContext = vm };
                var window = new Window { Width = 800, Height = 500, Content = view };
                window.Show(); window.UpdateLayout();
                try
                {
                    var input = view.FindControl<TextBox>("FileTextInput")!;
                    input.Text = """{"line":"草稿"}""";
                    await project.Services.GetRequiredService<LocalizationAuthoringService>().ApplyAsync(store.Prepare(store.PrepareText(target, "新")), target.File.Path);
                    window.UpdateLayout();
                    Assert.That(input.Text, Does.Contain("草稿"));
                    Assert.That(buffer.Error, Is.Not.Empty);
                    buffer.CancelCommand.Execute(null); window.UpdateLayout();
                    Assert.That(LocalizationJson.Text(Encoding.UTF8.GetBytes(input.Text!), "line"), Is.EqualTo("新"));
                    Assert.That(buffer.Error, Is.Empty);
                }
                finally { window.Close(); }
            });
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public Task LocalizationP2CatalogAndMetadataHaveDedicatedUndoableDocuments() => WithLocalizationProject(async project =>
    {
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var catalog = store.CaptureFile(LocalizationJson.CatalogPath);
        var metadata = store.CaptureFile(LocalizationJson.MetadataPath);
        var patches = new[]
        {
            new LocalizationFilePatch(catalog, Encoding.UTF8.GetBytes("""[{"id":"character_7","displayNameKey":"name","extension":"kept"}]""")),
            new LocalizationFilePatch(metadata, Encoding.UTF8.GetBytes("""{"version":1,"characterHighWater":7,"extension":"kept"}"""))
        };
        await project.Services.GetRequiredService<LocalizationAuthoringService>().ApplyAsync(store.Prepare(patches), "catalog and metadata");
        var session = project.Services.GetRequiredService<IProjectContentSession>();
        await session.SaveAsync();
        Assert.That(File.ReadAllText(Path.Combine(project.RootPath, LocalizationJson.MetadataPath)), Does.Contain("kept"));
        Assert.That(project.Services.GetRequiredService<ProjectTextFiles>().Open(LocalizationJson.CatalogPath).IsReadOnly, Is.True);
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        await history.UndoAsync();
        Assert.That(File.Exists(Path.Combine(project.RootPath, LocalizationJson.CatalogPath)), Is.False);
        await history.RedoAsync();
        Assert.That(store.CaptureFile(LocalizationJson.MetadataPath).Data, Is.EqualTo(patches[1].After));
    });

    [Test]
    public Task LocalizationP2ExternalConflictKeepsDirtyContentAndSaveBaseline() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """{"line":"旧"}""");
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var target = store.CaptureText("line");
        await project.Services.GetRequiredService<LocalizationAuthoringService>().SetTextAsync(target, "内存新值");
        WriteTranslation(project, """{"line":"外部新值"}""");
        var session = project.Services.GetRequiredService<IProjectContentSession>();
        try { await session.SaveAsync(); Assert.Fail("Expected external conflict."); } catch (EditorOperationException) { }
        Assert.That(session.IsDirty, Is.True);
        Assert.That(store.CaptureText("line").Text, Is.EqualTo("内存新值"));
        Assert.That(LocalizationJson.Text(File.ReadAllBytes(Path.Combine(project.RootPath, target.File.Path)), target.Key), Is.EqualTo("外部新值"));
    });

    [Test]
    public Task LocalizationP2CharacterReservationsAreDurableDoNotSaveOtherContentAndSurviveReopen() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """[{"id":"character_12","displayNameKey":"name"}]""", LocalizationJson.CatalogPath);
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        documents.CurrentDocument.Name = "Unsaved";
        Assert.That(await store.ReserveCharacterKeyAsync(), Is.EqualTo("character_13"));
        Assert.That(await store.ReserveCharacterKeyAsync(), Is.EqualTo("character_14"));
        var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(project.RootPath, LocalizationJson.MetadataPath)))!;
        Assert.That(metadata["characterHighWater"]!.GetValue<long>(), Is.EqualTo(14));
        Assert.That(project.Services.GetRequiredService<EditorHistories>().Content.CanUndo, Is.False);
        using (var graph = JsonDocument.Parse(File.ReadAllText(Path.Combine(project.RootPath, "Graph/graph.json"))))
            Assert.That(graph.RootElement.GetProperty("name").GetString(), Is.EqualTo("Localization"));
        var projects = project.Services.GetRequiredService<IProjectService>();
        await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(await reopened.Services.GetRequiredService<LocalizationDocumentStore>().ReserveCharacterKeyAsync(), Is.EqualTo("character_15"));
    });

    [TestCase(false)]
    [TestCase(true)]
    public async Task LocalizationP2NewFilePublicationFailureRestoresDiskOrRequiresRepair(bool failRecovery)
    {
        var transaction = new LocalizationPublicationFault { FailRecovery = failRecovery };
        await WithLocalizationProject(async project =>
        {
            var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
            var target = store.CaptureText("line");
            var history = project.Services.GetRequiredService<EditorHistories>().Content;
            transaction.Armed = true;
            try { await project.Services.GetRequiredService<LocalizationAuthoringService>().SetTextAsync(target, "正文"); Assert.Fail("Expected publication failure."); }
            catch (IOException) { }
            Assert.That(history.CanUndo, Is.False);
            var session = project.Services.GetRequiredService<IProjectContentSession>();
            Assert.That(session.RepairRequired, Is.EqualTo(failRecovery));
            if (failRecovery) Assert.That(() => store.CaptureText("line"), Throws.InvalidOperationException);
            else
            {
                Assert.That(File.Exists(Path.Combine(project.RootPath, target.File.Path)), Is.False);
                Assert.That(store.CaptureText("line").Text, Is.Null);
            }
        }, transaction);
    }

    private sealed class LocalizationPublicationFault : GalNet.Editor.Shared.Commands.AuthorFileTransaction
    {
        public bool Armed;
        public bool FailRecovery;
        protected override void Publish(string path, string? stagedFile)
        {
            base.Publish(path, stagedFile);
            if (Armed && path.Replace('\\', '/').Contains("I18n/", StringComparison.Ordinal))
            { Armed = false; throw new IOException("Injected localization publication failure."); }
        }
        protected override void Restore(string path, string? backupFile)
        {
            if (FailRecovery && path.Replace('\\', '/').Contains("I18n/", StringComparison.Ordinal)) throw new IOException("Injected recovery failure.");
            base.Restore(path, backupFile);
        }
    }
}
