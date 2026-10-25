using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [TestCase(true)]
    [TestCase(false)]
    public Task I18nQueuedLanguageChangeAndTextCommitRecheckInsideHistory(bool languageFirst) => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        await service.SetAsync(service.Capture(item.Group, "line", "en-US"), "English");
        var draft = service.Capture(item.Group, "line", "en-US"); var preview = service.Preview("en-US");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var held = history.ExecuteAsync(new CallbackEdit("Hold history", async () => { started.SetResult(); await release.Task; }, () => ValueTask.CompletedTask));
        await started.Task;
        Task change, write;
        if (languageFirst) { change = service.ChangeDevelopmentLanguageAsync(preview); write = service.SetAsync(draft, "Queued translation"); }
        else { write = service.SetAsync(draft, "Queued translation"); change = service.ChangeDevelopmentLanguageAsync(preview); }
        release.SetResult(); await held.WaitAsync(TimeSpan.FromSeconds(10));
        if (languageFirst)
        {
            await change; Assert.ThrowsAsync<EditorOperationException>(async () => await write);
            Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("English"));
        }
        else
        {
            await write; Assert.ThrowsAsync<EditorOperationException>(async () => await change);
            Assert.That(project.Settings.TargetLocale.Code, Is.EqualTo("zh-CN"));
        }
    });

    [Test]
    public Task I18nLanguagesNormalizeRejectDuplicatesPinDevelopmentColumnAndPersistOrder() => WithLocalizationProject(async project =>
    {
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await panel.AddLanguageAsync(" en_us "); await panel.AddLanguageAsync("ja-JP");
        Assert.That(panel.Languages, Is.EqualTo(new[] { "zh-CN", "en-US", "ja-JP" }));
        Assert.ThrowsAsync<EditorOperationException>(async () => await panel.AddLanguageAsync("EN-us"));
        Assert.ThrowsAsync<EditorOperationException>(async () => await panel.AddLanguageAsync("bad/locale"));
        await panel.MoveLanguageAsync("en-US", -1); await panel.MoveLanguageAsync("zh-CN", 1);
        Assert.That(panel.Languages[0], Is.EqualTo("zh-CN"));
        await panel.MoveLanguageAsync("ja-JP", -1);
        var expected = new[] { "zh-CN", "ja-JP", "en-US" }; Assert.That(panel.Languages, Is.EqualTo(expected));
        await history.UndoAsync(); Assert.That(panel.Languages[1], Is.EqualTo("en-US")); await history.RedoAsync();
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Services.GetRequiredService<GameLocalizationPanelService>().Languages, Is.EqualTo(expected));
    });

    [Test]
    public Task I18nDevelopmentLanguageRequiresFreshMissingTextPreviewAndUndoPreservesOtherSettings() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>(); await panel.AddLanguageAsync("en-US");
        var preview = panel.Preview("en-US"); Assert.That(preview.Missing, Is.EqualTo(1));
        WriteTranslation(project, """{"line":"English"}""", "I18n/en-US/lines.json");
        Assert.ThrowsAsync<EditorOperationException>(async () => await panel.ChangeDevelopmentLanguageAsync(preview));
        await panel.ChangeDevelopmentLanguageAsync(panel.Preview("en-US"));
        Assert.That(project.Settings.TargetLocale.Code, Is.EqualTo("en-US"));
        Assert.That(project.Services.GetRequiredService<GameTextAuthoringService>().Read("line"), Is.EqualTo("English"));
        project.Settings.DefaultWidth = 1280;
        await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
        Assert.That(project.Settings.TargetLocale.Code, Is.EqualTo("zh-CN")); Assert.That(project.Settings.DefaultWidth, Is.EqualTo(1280));
        Assert.That(item.Entry.Parameters["content"].GetString(), Is.EqualTo("line"));
    });

    [Test]
    public Task I18nCategoriesIncludeUnopenedGroupsRolesBranchesAndDeduplicateSharedText() => WithLocalizationProject(async project =>
    {
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); var character = await characters.CreateAsync("姓名");
        var item = CharacterEntry(project); _ = CharacterEntry(project);
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var group = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 400, 0);
        var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 600, 0); branch.Source.Options![0].Text = "choice";
        WriteTranslation(project, """{"line":"正文","choice":"选择"}""");
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>();
        var snapshot = panel.Snapshot(item.Group);
        Assert.That(snapshot.Categories.Any(category => category.Id == group.Id), Is.True);
        Assert.That(snapshot.Rows, Has.Count.EqualTo(1)); Assert.That(snapshot.Rows[0].References, Has.Count.EqualTo(2));
        Assert.That(panel.Snapshot("characters").Rows.Single().Key, Is.EqualTo(character.DisplayNameKey));
        Assert.That(panel.Snapshot("branches").Rows.Single().Key, Is.EqualTo("choice"));
    });

    [Test]
    public Task I18nTranslationEditingSharesSourceAndMetadataHistoryAndRejectsStaleSourceOrRemovedRow() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文","keep":"保留"}""");
        var panel = project.Services.GetRequiredService<GameLocalizationPanelService>(); await panel.AddLanguageAsync("en-US");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await panel.SetAsync(panel.Capture(item.Group, "line", "en-US"), "English");
        Assert.That(panel.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("English"));
        var metadata = project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureFile(LocalizationJson.MetadataPath);
        Assert.That(LocalizationTextState.NeedsReview(metadata.Data, "line", "en-US"), Is.False);
        await history.UndoAsync(); Assert.That(panel.Snapshot(item.Group).Index.FindDirect("en-US", "line"), Is.Null);
        await history.RedoAsync();
        var stale = panel.Capture(item.Group, "line", "en-US");
        await SetGameText(project, item.Group, item.Entry, "改过的原文");
        Assert.ThrowsAsync<EditorOperationException>(async () => await panel.SetAsync(stale, "stale"));
        Assert.That(LocalizationTextState.NeedsReview(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureFile(LocalizationJson.MetadataPath).Data, "line", "en-US"), Is.True);
        var removed = panel.Capture(item.Group, "line", "en-US"); item.Entry.Parameters["content"] = JsonSerializer.SerializeToElement("other");
        Assert.ThrowsAsync<EditorOperationException>(async () => await panel.SetAsync(removed, "removed"));
    });

    [Test]
    public Task I18nMissingSourceEmptyTranslationAndUndoSaveRedoRemainDistinct() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); var panel = project.Services.GetRequiredService<GameLocalizationPanelService>();
        await panel.AddLanguageAsync("en-US");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await panel.SetAsync(panel.Capture(item.Group, "line", "en-US"), "");
        var snapshot = panel.Snapshot(item.Group); Assert.That(snapshot.Index.FindDirect("zh-CN", "line"), Is.Null);
        Assert.That(snapshot.Index.FindDirect("en-US", "line")?.Text, Is.EqualTo(""));
        Assert.That(LocalizationTextState.NeedsReview(snapshot.Metadata, "line", "en-US"), Is.Null);
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        await content.SaveAsync(); await history.UndoAsync(); await content.SaveAsync(); await history.RedoAsync(); await content.SaveAsync();
        Assert.That(panel.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo(""));
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Services.GetRequiredService<GameLocalizationPanelService>().Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo(""));
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await panel.SetAsync(panel.Capture(item.Group, "line", "en-US"), "closed"));
    });

    [Test]
    public Task I18nRelatedJsonDraftsRebaseUnrelatedLeavesAndRejectInvalidOrConflictingText() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        WriteTranslation(project, """{"line":"Old","keep":"before"}""", "I18n/en-US/lines.json");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        var file = store.CaptureText("line", "en-US").File; var files = project.Services.GetRequiredService<ProjectTextFiles>();
        var buffer = files.Open(file.Path); var draft = service.Capture(item.Group, "line", "en-US");
        buffer.Text = """{"line":"Old","keep":"after"}""";
        await service.SetAsync(draft, "New");
        Assert.That(store.CaptureText("keep", "en-US").Text, Is.EqualTo("after"));
        Assert.That(store.CaptureText("line", "en-US").Text, Is.EqualTo("New"));
        draft = service.Capture(item.Group, "line", "en-US"); buffer.Text = "{invalid";
        Assert.CatchAsync(async () => await service.SetAsync(draft, "blocked")); Assert.That(buffer.Text, Is.EqualTo("{invalid"));
        buffer.Text = """{"line":"Concurrent","keep":"after"}""";
        Assert.ThrowsAsync<EditorOperationException>(async () => await service.SetAsync(draft, "stale"));
        Assert.That(store.CaptureText("line", "en-US").Text, Is.EqualTo("Concurrent"));
    });

    [Test]
    public Task I18nFailedImmediateUpdateSurvivesSourceChangeAndCategorySwitchWithoutOverwriting() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var held = history.ExecuteAsync(new CallbackEdit("Hold", async () => { started.SetResult(); await release.Task; }, () => ValueTask.CompletedTask));
        await started.Task;
        var sourceChange = SetGameText(project, item.Group, item.Entry, "新原文");
        var cell = model.Rows.Single().Cells[1]; cell.Text = "pending";
        model.Selected = model.Categories.First();
        release.SetResult(); await held; await sourceChange;
        Assert.ThrowsAsync<EditorOperationException>(async () => await cell.CommitAsync()); model.Refresh();
        model.Selected = model.Categories.Single(category => category.Id == item.Group);
        Assert.That(model.Rows.Single().Cells[1], Is.SameAs(cell)); Assert.That(cell.Text, Is.EqualTo("pending"));
        Assert.ThrowsAsync<EditorOperationException>(async () => await cell.CommitAsync());
        Assert.That(cell.Text, Is.EqualTo("pending")); Assert.That(cell.Message, Is.Not.Empty);
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line"), Is.Null);
        cell.Revert(); Assert.That(cell.Text, Is.Empty); Assert.That(cell.IsDirty, Is.False);
    });

    [Test]
    public Task I18nSaveAllCommitsDraftsFromInactiveCategoriesAndNewSourceInvalidatesReviewProof() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        var cell = model.Rows.Single().Cells[1]; cell.Text = "Saved from another category";
        model.Selected = model.Categories.First();
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Saved from another category"));
        Assert.That(cell.IsDirty, Is.False);
        await service.ChangeDevelopmentLanguageAsync(service.Preview("en-US")); model.Refresh();
        model.Selected = model.Categories.Single(category => category.Id == item.Group);
        Assert.That(model.Rows.Single().Cells[1].Text, Is.EqualTo("原文"));
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync(); var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Settings.TargetLocale.Code, Is.EqualTo("en-US"));
        Assert.That(reopened.Services.GetRequiredService<GameLocalizationPanelService>().Languages[0], Is.EqualTo("en-US"));
    });

    [Test]
    public async Task I18nMountedTableUpdatesImmediatelyProtectsCompositionAndRetainsViewState()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
                var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US"); await service.AddLanguageAsync("ja-JP");
                var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
                var view = new GameLocalizationPanelView { DataContext = model }; var window = new Window { Content = view, Width = 1400, Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    TextBox Input(string culture) => view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "LocalizationCellInput" && box.DataContext is LocalizationGridCell cell && cell.Culture == culture);
                    var input = Input("en-US"); input.Focus(); input.Text = "First\nSecond";
                    input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
                    await PumpCharacterUi(() => service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text == "First\nSecond");
                    Assert.That(input.IsFocused, Is.True, "Async commit preserves input focus.");
                    var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single(); presenter.PreeditText = "拼";
                    input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
                    Dispatcher.UIThread.RunJobs();
                    Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("First\nSecond"));
                    presenter.PreeditText = null; input.Text = "组字完成";
                    await PumpCharacterUi(() => service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text == "组字完成");
                    Assert.That(input.IsFocused, Is.True);
                    input.Text = "updated";
                    await PumpCharacterUi(() => service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text == "updated");
                    input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                    Assert.That(input.Text, Is.EqualTo("updated"), "Escape does not undo successfully updated text.");
                    var cell = model.Rows.Single().Cells[1]; cell.Text = "kept update"; await cell.CommitAsync();
                    model.SetColumnWidth("en-US", 310);
                    window.Content = null; Dispatcher.UIThread.RunJobs();
                    var reopened = new GameLocalizationPanelView { DataContext = project.Services.GetRequiredService<GameLocalizationPanelViewModel>() }; window.Content = reopened; Dispatcher.UIThread.RunJobs();
                    Assert.That(model.Rows.Single().Cells[1].Text, Is.EqualTo("kept update"));
                    Assert.That(model.ColumnWidth("en-US"), Is.EqualTo(310));
                    await service.MoveLanguageAsync("ja-JP", -1);
                    Assert.That(model.Languages[1], Is.EqualTo("ja-JP"));
                    var history = project.Services.GetRequiredService<EditorHistories>().Content; await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
                    project.Services.GetRequiredService<IEditorLocalizationService>().ApplyLocale("ja-JP"); Dispatcher.UIThread.RunJobs();
                    Assert.That(history.IsDirty, Is.False); Assert.That(service.Languages[0], Is.EqualTo("zh-CN"));
                    Assert.That(reopened.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "line"), Is.False, "Normal table does not expose text keys.");
                    var dock = project.Services.GetRequiredService<EditorDockFactory>(); var layout = dock.CreateLayout(); dock.InitLayout(layout);
                    dock.OpenPanel(EditorDockPanelIds.Localization); dock.OpenPanel(EditorDockPanelIds.Localization);
                    Assert.That(dock.HasGlobalPanel(EditorDockPanelIds.Localization), Is.True);
                    var dockDraft = model.Rows.Single().Cells.Single(cell => cell.Culture == "en-US"); dockDraft.Text = "dock draft";
                    dock.ToggleGlobalPanel(EditorDockPanelIds.Localization); Assert.That(model.IsDisposed, Is.False);
                    dock.OpenPanel(EditorDockPanelIds.Localization); Assert.That(project.Services.GetRequiredService<GameLocalizationPanelViewModel>(), Is.SameAs(model));
                    await dockDraft.CommitAsync(); Assert.That(dockDraft.Text, Is.EqualTo("dock draft")); Assert.That(dockDraft.IsDirty, Is.False);
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }

    [Test]
    public Task I18nScriptLanguageRemainsCanonicalAcrossDevelopmentAndSourceAuthoring() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"原文"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("ZH_hans_cn");
        Assert.That(project.Settings.AvailableLocales[^1].Code, Is.EqualTo("zh-Hans-CN"));
        await service.SetAsync(service.Capture(item.Group, "line", "zh-Hans-CN"), "简体");
        await service.ChangeDevelopmentLanguageAsync(service.Preview("zh-Hans-CN"));
        await SetGameText(project, item.Group, item.Entry, "开发原文");
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("zh-Hans-CN", "line")?.Text, Is.EqualTo("开发原文"));
    });

    [Test]
    public async Task I18nMountedCategoriesAndRowsVirtualizeAndScrollToTheLastText()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var documents = project.Services.GetRequiredService<IEditorDocumentService>(); var item = CharacterEntry(project);
                var translations = new Dictionary<string, string> { ["line"] = "First" };
                for (var i = 0; i < 180; i++)
                {
                    var key = "row_" + i; translations[key] = "Text " + i;
                    documents.Groups[item.Group].Entries.Add(new()
                    {
                        StableId = Guid.NewGuid().ToString("N"),
                        Type = item.Entry.Type,
                        Parameters = new() { ["speaker"] = JsonSerializer.SerializeToElement(""), ["content"] = JsonSerializer.SerializeToElement(key) }
                    });
                }
                for (var i = 0; i < 55; i++) documents.CurrentDocument.Nodes.Add(new() { Id = "group_" + i, Type = "Group", Name = "Group " + i });
                WriteTranslation(project, JsonSerializer.Serialize(translations));
                var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
                var view = new GameLocalizationPanelView { DataContext = model }; var window = new Window { Content = view, Width = 1000, Height = 650 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var rows = view.FindControl<ListBox>("LocalizationRows")!; var categories = view.FindControl<ListBox>("LocalizationCategories")!;
                    Assert.That(rows.GetRealizedContainers().Count(), Is.LessThan(model.Rows.Count / 2));
                    Assert.That(categories.GetRealizedContainers().Count(), Is.LessThan(model.Categories.Count));
                    rows.ScrollIntoView(model.Rows[^1]); categories.ScrollIntoView(model.Categories[^1]); Dispatcher.UIThread.RunJobs();
                    await PumpCharacterUi(() => view.GetVisualDescendants().OfType<TextBox>().Any(box => box.Text == "Text 179"));
                    Assert.That(rows.GetRealizedContainers().Count(), Is.LessThan(model.Rows.Count / 2));
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }
}
