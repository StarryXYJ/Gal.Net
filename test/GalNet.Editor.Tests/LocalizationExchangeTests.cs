using GalNet.Contracts.Builtins.Assets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public Task ExchangeTemplateRoundTripKeepsStableKeysTranslationsHistoryAndRuntimeContent() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>(); await panel.AddLanguageAsync("en-US");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>();
        var json = await exchange.ExportAsync("en-US", null, false);
        var template = JsonNode.Parse(json)!; template["rows"]![0]!["translation"] = "English";
        var preview = exchange.Preview(template.ToJsonString(), "en-US");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await exchange.ApplyAsync(preview, ["line"], false);
        Assert.That(panel.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("English"));
        Assert.That(item.Entry.Parameters["content"].GetString(), Is.EqualTo("line"));
        await history.UndoAsync(); Assert.That(panel.Snapshot(item.Group).Index.FindDirect("en-US", "line"), Is.Null);
        await history.RedoAsync(); Assert.That(history.CanUndo, Is.True);
        var content = project.Services.GetRequiredService<IProjectContentSession>(); await content.SaveAsync();
        var identity = JsonNode.Parse(json)!["projectId"]!.GetValue<string>();
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync(); var reopened = await projects.OpenAsync(project.RootPath);
        var restored = reopened.Services.GetRequiredService<LocalizationExchangeService>(); Assert.That(JsonNode.Parse(await restored.ExportAsync("en-US", null, false))!["projectId"]!.GetValue<string>(), Is.EqualTo(identity));
        var result = await new ProjectContentBuilder(reopened.Services.GetRequiredService<IEntryCatalog>()).BuildAsync(reopened.RootPath);
        var provider = new ProjectGameContentProvider(result.OutputPath, BuiltinResourceTypes.CreateCatalog(), BuiltinGalleryTypes.CreateCatalog(BuiltinResourceTypes.CreateCatalog()));
        var loaded = await provider.LoadAsync(); Assert.That(loaded.Graph.Nodes, Is.Not.Empty);
        var sources = loaded.Localization.Sources.Select(source =>
        {
            var values = LocalizationJson.Texts(System.Text.Encoding.UTF8.GetBytes(source.Json));
            return new GalNet.Runtime.I18n.LocalizedTextSource(System.Globalization.CultureInfo.GetCultureInfo(source.Culture), key => values.GetValueOrDefault(key));
        });
        var culture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        var resolver = new GalNet.Runtime.I18n.GameTextResolver(sources, culture, () => culture);
        Assert.That(resolver.Resolve("line"), Is.EqualTo("原文")); culture = System.Globalization.CultureInfo.GetCultureInfo("en-US"); Assert.That(resolver.Resolve("line"), Is.EqualTo("English"));
        Assert.That(JsonNode.Parse(await restored.ExportAsync("en-US", null, true))!["line"]!.GetValue<string>(), Is.EqualTo("English"));
    });

    [Test]
    public Task ExchangeExternalRowOrderAndCharacterKeyRenameDoNotChangeTranslationIdentity() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); var second = CharacterEntry(project); second.Entry.Parameters["content"] = JsonSerializer.SerializeToElement("second");
        WriteTranslation(project, """{"line":"一","second":"二"}""");
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); var character = await characters.CreateAsync("姓名", "hero");
        await project.Services.GetRequiredService<GameLocalizationPanelService>().AddLanguageAsync("en-US");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>(); var template = JsonNode.Parse(await exchange.ExportAsync("en-US", null, false))!;
        await characters.UpdateAsync(character, "new-hero", "姓名");
        var rows = template["rows"]!.AsArray().Reverse().Select(row => row!.DeepClone()).ToArray();
        foreach (var row in rows) row["translation"] = "translated " + row["key"]!.GetValue<string>();
        template["rows"] = new JsonArray(rows);
        var preview = exchange.Preview(template.ToJsonString(), "en-US"); await exchange.ApplyAsync(preview, preview.Rows.Select(row => row.Row.Key).ToArray(), false);
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        Assert.That(store.CaptureText("line", "en-US").Text, Is.EqualTo("translated line"));
        Assert.That(store.CaptureText(character.DisplayNameKey, "en-US").Text, Is.EqualTo("translated " + character.DisplayNameKey));
        Assert.That(characters.Rows().Single().Id, Is.EqualTo("new-hero"));
    });

    [Test]
    public Task ExchangeQueuedImportCannotBecomeADevelopmentLanguageOverwrite() => WithLocalizationProject(async project =>
    {
        CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>(); await panel.AddLanguageAsync("en-US");
        var group = panel.Snapshot("characters").Categories.First(category => category.Id is not ("characters" or "branches")).Id;
        await panel.SetAsync(panel.Capture(group, "line", "en-US"), "Original English");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>(); var preview = exchange.Preview("""{"line":"stale"}""", "en-US");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var held = history.ExecuteAsync(new CallbackEdit("hold", async () => { started.SetResult(); await release.Task; }, () => ValueTask.CompletedTask));
        await started.Task; var change = panel.ChangeDevelopmentLanguageAsync(panel.Preview("en-US")); var import = exchange.ApplyAsync(preview, ["line"], false);
        release.SetResult(); await held; await change;
        Assert.ThrowsAsync<EditorOperationException>(async () => await import);
        Assert.That(project.Services.GetRequiredService<GameTextAuthoringService>().Read("line"), Is.EqualTo("Original English"));
    });

    [Test]
    public Task ExchangeIdentityReservationSurvivesOlderSourceUndoWithoutSavingDirtyText() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"旧"}""");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await SetGameText(project, item.Group, item.Entry, "新");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>();
        var first = JsonNode.Parse(await exchange.ExportAsync("zh-CN", null, false))!["projectId"]!.GetValue<string>();
        Assert.That(File.ReadAllText(Path.Combine(project.RootPath, "I18n/zh-CN/lines.json")), Does.Contain("旧"));
        await history.UndoAsync(); Assert.That(project.Services.GetRequiredService<GameTextAuthoringService>().Read("line"), Is.EqualTo("旧"));
        await history.RedoAsync(); var second = JsonNode.Parse(await exchange.ExportAsync("zh-CN", null, false))!["projectId"]!.GetValue<string>();
        Assert.That(second, Is.EqualTo(first));
    });

    [Test]
    public Task ExchangeSourceRevisionMarksOldTemplateTranslationForReviewAndFlatInputStaysUnknown() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"旧"}""");
        await project.Services.GetRequiredService<GameLocalizationPanelService>().AddLanguageAsync("en-US");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>(); var template = JsonNode.Parse(await exchange.ExportAsync("en-US", null, false))!;
        template["rows"]![0]!["translation"] = "English"; await SetGameText(project, item.Group, item.Entry, "新");
        var preview = exchange.Preview(template.ToJsonString(), "en-US"); Assert.That(preview.Rows.Single().SourceChanged, Is.True);
        await exchange.ApplyAsync(preview, ["line"], false);
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        Assert.That(LocalizationTextState.NeedsReview(store.CaptureFile(LocalizationJson.MetadataPath).Data, "line", "en-US"), Is.True);
        await exchange.ApplyAsync(exchange.Preview("""{"line":"flat"}""", "en-US"), ["line"], false);
        Assert.That(LocalizationTextState.NeedsReview(store.CaptureFile(LocalizationJson.MetadataPath).Data, "line", "en-US"), Is.Null);
    });

    [Test]
    public Task ExchangeEmptyNullUnknownSelectionAndStalePreviewsNeverBlindlyOverwrite() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        await project.Services.GetRequiredService<GameLocalizationPanelService>().AddLanguageAsync("en-US");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>();
        var preview = exchange.Preview("""{"line":"","unknown":"x","null":null}""", "en-US");
        Assert.CatchAsync(async () => await exchange.ApplyAsync(preview, ["line"], false));
        Assert.CatchAsync(async () => await exchange.ApplyAsync(preview, ["unknown"], true));
        await exchange.ApplyAsync(preview, ["line"], true);
        Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText("line", "en-US").Text, Is.EqualTo(""));
        preview = exchange.Preview("""{"line":"pending"}""", "en-US"); await SetGameText(project, item.Group, item.Entry, "新原文");
        Assert.ThrowsAsync<EditorOperationException>(async () => await exchange.ApplyAsync(preview, ["line"], false));
        Assert.Throws<EditorOperationException>(() => exchange.Preview("""{"line":"source overwrite"}""", "zh-CN"));
    });

    [Test]
    public Task ExchangeRejectsWrongProjectAndProtectsAuthorPathsAndFailedOutput() => WithLocalizationProject(async project =>
    {
        CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        await project.Services.GetRequiredService<GameLocalizationPanelService>().AddLanguageAsync("en-US");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>(); var template = JsonNode.Parse(await exchange.ExportAsync("en-US", null, false))!;
        template["projectId"] = Guid.NewGuid().ToString("N"); Assert.Throws<EditorOperationException>(() => exchange.Preview(template.ToJsonString(), "en-US"));
        Assert.CatchAsync(async () => await exchange.ExportFileAsync(Path.Combine(project.RootPath, "I18n/zh-CN/lines.json"), "en-US", null, false));
        var target = Path.Combine(_root, "does-not-exist", "export.json"); Assert.CatchAsync(async () => await exchange.ExportFileAsync(target, "en-US", null, false));
        Assert.That(File.Exists(target), Is.False);
        var preview = exchange.Preview("""{"line":"late"}""", "en-US"); await project.Services.GetRequiredService<IProjectService>().CloseAsync();
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await exchange.ApplyAsync(preview, ["line"], false));
    });

    [Test]
    public Task ExchangeBatchJsonDraftConflictPreservesInputAndDoesNotPartiallyApply() => WithLocalizationProject(async project =>
    {
        CharacterEntry(project); var second = CharacterEntry(project); second.Entry.Parameters["content"] = JsonSerializer.SerializeToElement("second");
        WriteTranslation(project, """{"line":"一","second":"二"}""");
        WriteTranslation(project, """{"line":"old","second":"old2"}""", "I18n/en-US/lines.json");
        await project.Services.GetRequiredService<GameLocalizationPanelService>().AddLanguageAsync("en-US");
        var exchange = project.Services.GetRequiredService<LocalizationExchangeService>(); var preview = exchange.Preview("""{"line":"new","second":"new2"}""", "en-US");
        var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("I18n/en-US/lines.json"); buffer.Text = "{broken";
        Assert.CatchAsync(async () => await exchange.ApplyAsync(preview, ["line", "second"], false));
        Assert.That(buffer.Text, Is.EqualTo("{broken"));
        Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText("line", "en-US").Text, Is.EqualTo("old"));
        buffer.Text = """{"line":"concurrent","second":"old2"}""";
        Assert.ThrowsAsync<EditorOperationException>(async () => await exchange.ApplyAsync(preview, ["line", "second"], false));
        Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText("second", "en-US").Text, Is.EqualTo("old2"));
    });

    [Test]
    public async Task ExchangeMountedExportImportPreviewAndApplyUseFileDialogsAndOneHistory()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
                await project.Services.GetRequiredService<GameLocalizationPanelService>().AddLanguageAsync("en-US");
                var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.ExchangeLanguage = "en-US";
                var dialogs = (TestDialogs)project.Services.GetRequiredService<IFileDialogService>(); dialogs.SavePath = Path.Combine(_root, "template.json");
                var view = new GameLocalizationPanelView { DataContext = model }; var window = new Window { Content = view, Width = 1300, Height = 850 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var options = view.FindControl<Button>("ExportOptions")!;
                    Click(window, options); Dispatcher.UIThread.RunJobs();
                    var flyout = (Flyout)options.Flyout!;
                    try
                    {
                        var export = ((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ExportLocalization");
                        var point = new Point(export.Bounds.Width / 2, export.Bounds.Height / 2);
                        var popupWindow = TopLevel.GetTopLevel(export)!;
                        var exportPoint = export.TranslatePoint(point, popupWindow)!.Value;
                        popupWindow.MouseDown(exportPoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                        popupWindow.MouseUp(exportPoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    }
                    finally { flyout.Hide(); }
                    await PumpCharacterUi(() => File.Exists(dialogs.SavePath));
                    var template = JsonNode.Parse(File.ReadAllText(dialogs.SavePath))!; template["rows"]![0]!["translation"] = "Imported"; File.WriteAllText(dialogs.SavePath, template.ToJsonString());
                    dialogs.OpenPaths = [dialogs.SavePath]; await model.PreviewImportCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                    Assert.That(model.HasImport, Is.True); Assert.That(model.ImportRows.Single().Include, Is.True);
                    Assert.That(view.FindControl<ListBox>("LocalizationImportRows")!.GetRealizedContainers(), Is.Not.Empty);
                    var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear(); await model.ApplyImportCommand.ExecuteAsync(null);
                    Assert.That(model.HasImport, Is.False); Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText("line", "en-US").Text, Is.EqualTo("Imported"));
                    await history.UndoAsync(); Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText("line", "en-US").Text, Is.Null);
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }
}
