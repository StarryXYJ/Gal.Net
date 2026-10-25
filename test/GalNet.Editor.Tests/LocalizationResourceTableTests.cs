using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    [Test]
    public Task I18nTypingDefersWritesAndRefreshesUntilCommitAndSaveEmpty() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        await service.SetAsync(service.Capture(item.Group, "line", "en-US"), "Before");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        var cell = model.Rows.Single().Cells[1];
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        var content = project.Services.GetRequiredService<IProjectContentSession>(); var changes = 0;
        content.Changed += () => changes++;
        var row = model.Rows.Single();
        cell.Text = "First"; cell.Text = "Second"; cell.Text = "Latest\n完整文本";
        await Task.Delay(20);
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Before"));
        Assert.That(history.CanUndo, Is.False); Assert.That(cell.Busy, Is.False);
        Assert.That(changes, Is.EqualTo(1), "Typing only notifies the first dirty transition, without per-key project refresh.");
        Assert.That(model.Rows.Single(), Is.SameAs(row));
        await cell.CommitAsync();
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Latest\n完整文本"));
        Assert.That(cell.IsDirty, Is.False); Assert.That(cell.Message, Is.Empty);
        await history.UndoAsync(); Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.Not.EqualTo("Latest\n完整文本"));
        await history.RedoAsync(); Assert.That(cell.Text, Is.EqualTo("Latest\n完整文本"));
        cell.Text = ""; await cell.CommitAsync();
        Assert.That(cell.IsMissing, Is.False); Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo(""));
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
    });

    [Test]
    public Task I18nInFlightCommitKeepsLaterTypingPendingUntilSaveAll() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        await service.SetAsync(service.Capture(item.Group, "line", "en-US"), "Before");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        var cell = model.Rows.Single().Cells[1];
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var held = history.ExecuteAsync(new CallbackEdit("Hold", async () => { started.SetResult(); await release.Task; }, () => ValueTask.CompletedTask));
        await started.Task;
        cell.Text = "Submitted"; var commit = cell.CommitAsync(); cell.Text = "Still typing";
        release.SetResult(); await held; await commit;
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Submitted"));
        Assert.That(cell.Text, Is.EqualTo("Still typing")); Assert.That(cell.IsDirty, Is.True);
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Still typing"));
        Assert.That(cell.IsDirty, Is.False);
    });

    [Test]
    public Task I18nCellCommitCanRetryAfterJsonRepairWithoutRetyping() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
        WriteTranslation(project, """{"line":"Before"}""", "I18n/en-US/lines.json");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        var cell = model.Rows.Single().Cells[1];
        var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("I18n/en-US/lines.json");
        buffer.Text = "{invalid"; cell.Text = "Retry unchanged input";
        Assert.CatchAsync(async () => await cell.CommitAsync());
        Assert.That(cell.Text, Is.EqualTo("Retry unchanged input")); Assert.That(cell.HasMessage, Is.True);
        buffer.Text = """{"line":"Before"}""";
        await cell.CommitAsync();
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Retry unchanged input"));
        Assert.That(cell.HasMessage, Is.False);
    });

    [Test]
    public Task I18nRemoveLanguageRetainsTranslationsSupportsUndoAndRejectsOldWrites() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        await service.SetAsync(service.Capture(item.Group, "line", "en-US"), "Retained");
        var target = service.Capture(item.Group, "line", "en-US");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        Assert.ThrowsAsync<EditorOperationException>(async () => await service.RemoveLanguageAsync("zh-CN"));
        await service.RemoveLanguageAsync("en-US"); Assert.That(service.Languages, Does.Not.Contain("en-US"));
        Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText("line", "en-US").Text, Is.EqualTo("Retained"));
        Assert.ThrowsAsync<EditorOperationException>(async () => await service.SetAsync(target, "stale"));
        await history.UndoAsync(); Assert.That(service.Languages, Does.Contain("en-US"));
        await history.RedoAsync(); await service.AddLanguageAsync("en_us");
        Assert.That(service.Snapshot(item.Group).Index.FindDirect("en-US", "line")?.Text, Is.EqualTo("Retained"));
    });

    [Test]
    public Task I18nSuggestionsExcludeSelectedLanguagesAndMissingFilterUsesVisibleColumns() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US"); await service.AddLanguageAsync("ja-JP");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        Assert.That(model.LanguageSuggestions.Any(value => value.StartsWith("en-US —")), Is.False);
        model.NewLanguage = "Korean"; Assert.That(model.LanguageSuggestions, Has.Some.StartsWith("ko-KR —"));
        model.NewLanguage = "ko-KR — 한국어(대한민국)"; await model.AddLanguageCommand.ExecuteAsync(null);
        Assert.That(model.Languages, Does.Contain("ko-KR"));
        model.SetLanguageVisible("ko-KR", false);
        var cell = model.Rows.Single().Cells.Single(value => value.Culture == "en-US"); cell.Text = "Translated"; await cell.CommitAsync();
        model.OnlyMissing = true; Assert.That(model.Rows, Has.Count.EqualTo(1));
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        model.SetLanguageVisible("ja-JP", false); Assert.That(model.Rows, Is.Empty);
        model.SetLanguageVisible("zh-CN", false); Assert.That(model.VisibleLanguages, Does.Contain("zh-CN"));
        model.SetLanguageVisible("ja-JP", true); Assert.That(model.Rows, Has.Count.EqualTo(1));
        Assert.That(history.CanUndo, Is.False);
    });

    [Test]
    public async Task I18nVisibleLanguageHeadersStayAlignedWhenTableBecomesNarrowerThanViewport()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
                var service = project.Services.GetRequiredService<GameLocalizationPanelService>();
                foreach (var culture in new[] { "en-US", "ja-JP", "ko-KR" }) await service.AddLanguageAsync(culture);
                var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
                var view = new GameLocalizationPanelView { DataContext = model }; var window = new Window { Content = view, Width = 1100, Height = 650 }; window.Show(); Dispatcher.UIThread.RunJobs();
                void CheckColumns()
                {
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    var headers = view.FindControl<StackPanel>("LanguageHeaders")!.Children.OfType<Grid>().ToArray();
                    var inputs = view.GetVisualDescendants().OfType<TextBox>().Where(input => input.Name == "LocalizationCellInput").ToArray();
                    Assert.That(inputs.Select(input => ((LocalizationGridCell)input.DataContext!).Culture), Is.EqualTo(model.VisibleLanguages));
                    Assert.That(headers, Has.Length.EqualTo(inputs.Length));
                    for (var i = 0; i < headers.Length; i++)
                    {
                        var cellBox = (Control)inputs[i].Parent!.Parent!;
                        Assert.That(headers[i].TranslatePoint(default, view)!.Value.X, Is.EqualTo(cellBox.TranslatePoint(default, view)!.Value.X).Within(1), "Header and cell must share the same left edge after hiding a column.");
                        Assert.That(headers[i].Bounds.Width, Is.EqualTo(cellBox.Bounds.Width).Within(1));
                    }
                }
                try
                {
                    CheckColumns();
                    view.FindControl<ScrollViewer>("LocalizationTableScroll")!.Offset = new Vector(160, 0);
                    model.SetLanguageVisible("en-US", false); CheckColumns();
                    model.SetLanguageVisible("ko-KR", false); CheckColumns();
                    model.SetLanguageVisible("ja-JP", false); CheckColumns();
                    model.SetLanguageVisible("en-US", true); CheckColumns();
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task I18nMountedManagerAddsAndRemovesLanguageAndTableUsesCompactOpaqueContexts()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source text"}""");
                var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
                var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
                var view = new GameLocalizationPanelView { DataContext = model }; var window = new Window { Content = view, Width = 1000, Height = 650 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var row = model.Rows.Single(); Assert.That(row.Source.Locations.Single().Details, Does.Contain(item.Entry.StableId));
                    Assert.That(row.Slot, Does.Contain("content"));
                    var context = view.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "LocalizationContext");
                    var brush = (ISolidColorBrush)context.Background!;
                    Assert.That(brush.Color.A, Is.EqualTo(255)); Assert.That(brush.Opacity, Is.EqualTo(1));
                    var source = view.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "LocalizationCellInput" && input.DataContext is LocalizationGridCell cell && cell.Culture == "zh-CN");
                    Assert.That(source.Bounds.Height, Is.LessThanOrEqualTo(48));
                    var resize = view.GetVisualDescendants().OfType<Thumb>().Single(thumb => thumb.Name == "ResizeLanguage_zh_CN");
                    var resizePoint = resize.TranslatePoint(new Point(resize.Bounds.Width / 2, resize.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(resizePoint, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    window.MouseMove(resizePoint + new Vector(40, 0)); Dispatcher.UIThread.RunJobs();
                    window.MouseUp(resizePoint + new Vector(40, 0), MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    Assert.That(model.ColumnWidth("zh-CN"), Is.GreaterThan(270));
                    var manage = view.FindControl<Button>("ManageLanguages")!; Click(window, manage); Dispatcher.UIThread.RunJobs();
                    var icon = (PathIcon)manage.Content!;
                    Assert.That(icon.Bounds.Width, Is.EqualTo(16)); Assert.That(icon.Bounds.Height, Is.EqualTo(16));
                    Assert.That(icon.TranslatePoint(default, manage)!.Value.X, Is.GreaterThanOrEqualTo(0));
                    var flyout = (Flyout)manage.Flyout!; var popup = (Control)flyout.Content!;
                    var selector = popup.GetVisualDescendants().OfType<AutoCompleteBox>().Single();
                    selector.Text = "ja-JP"; Dispatcher.UIThread.RunJobs();
                    var input = selector.GetVisualDescendants().OfType<TextBox>().First(); input.Focus();
                    input.Text = "ja"; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    await PumpCharacterUi(() => selector.IsDropDownOpen);
                    Assert.That(model.LanguageSuggestions.Any(value => value.StartsWith("ja-JP —")), Is.True);
                    Assert.That(model.LanguageSuggestions.Any(value => value.StartsWith("en-US —")), Is.False);
                    selector.Text = "ja-JP"; Dispatcher.UIThread.RunJobs();
                    Assert.That(model.NewLanguage, Is.EqualTo("ja-JP"));
                    var add = popup.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddLocalizationLanguage");
                    var top = TopLevel.GetTopLevel(add)!;
                    var point = add.TranslatePoint(new Point(add.Bounds.Width / 2, add.Bounds.Height / 2), top)!.Value;
                    top.MouseDown(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    top.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    await PumpCharacterUi(() => model.Languages.Contains("ja-JP"));
                    popup.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RemoveLanguage_ja_JP").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await PumpCharacterUi(() => !model.Languages.Contains("ja-JP"));
                    if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                    { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "localization-manager.png")); }
                    flyout.Hide(); Dispatcher.UIThread.RunJobs();
                    source = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "LocalizationCellInput" && box.DataContext is LocalizationGridCell cell && cell.Culture == "zh-CN");
                    source.Focus(); source.Text = "失焦更新"; Dispatcher.UIThread.RunJobs();
                    Assert.That(service.Snapshot(item.Group).Index.FindDirect("zh-CN", "line")?.Text, Is.EqualTo("Source text"));
                    view.FindControl<TextBox>("LocalizationSearch")!.Focus();
                    await PumpCharacterUi(() => service.Snapshot(item.Group).Index.FindDirect("zh-CN", "line")?.Text == "失焦更新");
                    Assert.That(view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "LocalizationCellInput" && box.DataContext is LocalizationGridCell cell && cell.Culture == "zh-CN"), Is.SameAs(source));
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }
}
