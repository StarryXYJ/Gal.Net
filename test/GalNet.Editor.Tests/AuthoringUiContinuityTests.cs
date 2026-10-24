using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task VariableSubpagesRestoreIdentityDraftAndBaselineAfterSwitchRefreshAndReopen()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "VariableSubpages"), "Variables", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        try
        {
            var author = project.Services.GetRequiredService<VariableAuthoringService>();
            var player = author.Add(VariableScope.Player); var save = author.Add(VariableScope.Save);
            var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
            var vm = project.Services.GetRequiredService<VariablesPanelViewModel>();
            vm.Selected = vm.Rows.Single(); vm.DraftName = "pendingPlayer";
            vm.ScopeIndex = 1; vm.Selected = vm.Rows.Single(); vm.DraftDescription = "pending save description";
            vm.ScopeIndex = 0;
            Assert.That(vm.Selected?.Uid, Is.EqualTo(player.DefaultValue.Uid)); Assert.That(vm.DraftName, Is.EqualTo("pendingPlayer"));
            Assert.That(history.CanUndo, Is.False);
            author.Update(VariableScope.Player, player.DefaultValue.Uid, "external", VariableType.Int, "42", "");
            vm.ScopeIndex = 1; Assert.That(vm.Selected?.Uid, Is.EqualTo(save.DefaultValue.Uid)); Assert.That(vm.DraftDescription, Is.EqualTo("pending save description"));
            vm.Dispose();
            using var reopened = project.Services.GetRequiredService<VariablesPanelViewModel>();
            Assert.That(reopened.ScopeIndex, Is.EqualTo(1)); Assert.That(reopened.Selected?.Uid, Is.EqualTo(save.DefaultValue.Uid));
            Assert.That(reopened.DraftDescription, Is.EqualTo("pending save description"));
            reopened.ScopeIndex = 0; reopened.ApplyCommand.Execute(null);
            Assert.That(reopened.MessageText, Is.Not.Empty); Assert.That(author.Get(VariableScope.Player).Single().Name, Is.EqualTo("external"));
            reopened.RevertCommand.Execute(null); Assert.That(reopened.DraftName, Is.EqualTo("external"));
            reopened.DraftName = "retained";
            author.Delete(VariableScope.Player, player.DefaultValue.Uid);
            Assert.That(reopened.Selected, Is.Null); Assert.That(reopened.MissingSelection, Is.True);
            var replacement = author.Add(VariableScope.Player);
            Assert.That(reopened.Selected, Is.Null, "A new UID must not acquire the old draft.");
            await history.UndoAsync(); await history.UndoAsync();
            Assert.That(reopened.Selected?.Uid, Is.EqualTo(player.DefaultValue.Uid)); Assert.That(reopened.DraftName, Is.EqualTo("retained"));
            reopened.Search = "no matching variable"; Assert.That(reopened.Rows, Is.Empty);
            reopened.Search = ""; Assert.That(reopened.Selected?.Uid, Is.EqualTo(player.DefaultValue.Uid)); Assert.That(reopened.DraftName, Is.EqualTo("retained"));
        }
        finally { await projects.CloseAsync(); }
    }

    [Test]
    public Task CharacterDraftsFollowStableNameIdentityAcrossFilterRenameSelectionAndReopen() => WithLocalizationProject(async project =>
    {
        var author = project.Services.GetRequiredService<CharacterAuthoringService>();
        var first = await author.CreateAsync("First", "first"); var second = await author.CreateAsync("Second", "second");
        var vm = project.Services.GetRequiredService<CharactersPanelViewModel>(); vm.Selected = vm.Rows.Single(row => row.Id == first.Id);
        vm.DraftName = "Unapplied first"; vm.Search = "Second"; Assert.That(vm.Selected, Is.Null);
        vm.Search = ""; Assert.That(vm.Selected?.DisplayNameKey, Is.EqualTo(first.DisplayNameKey)); Assert.That(vm.DraftName, Is.EqualTo("Unapplied first"));
        vm.Selected = vm.Rows.Single(row => row.Id == second.Id); vm.DraftName = "Unapplied second";
        vm.Selected = vm.Rows.Single(row => row.Id == first.Id); Assert.That(vm.DraftName, Is.EqualTo("Unapplied first"));
        await author.UpdateAsync(first, "external", "External name");
        Assert.That(vm.Selected?.Id, Is.EqualTo("external")); Assert.That(vm.DraftName, Is.EqualTo("Unapplied first"));
        await vm.ApplyCommand.ExecuteAsync(null); Assert.That(vm.Message, Is.Not.Empty);
        vm.RevertCommand.Execute(null); Assert.That(vm.DraftName, Is.EqualTo("External name"));
        vm.Selected = vm.Rows.Single(row => row.Id == second.Id); vm.Dispose();
        using var reopened = project.Services.GetRequiredService<CharactersPanelViewModel>();
        Assert.That(reopened.Selected?.Id, Is.EqualTo(second.Id)); Assert.That(reopened.DraftName, Is.EqualTo("Unapplied second"));
    });

    [Test]
    public Task CharactersSharingANameTextNeverShareDraftsOrAcquireADeletedCharactersSelection() => WithLocalizationProject(async project =>
    {
        WriteTranslation(project, """[{"id":"first","displayNameKey":"shared"},{"id":"second","displayNameKey":"shared"}]""", "I18n/catalog/characters.json");
        WriteTranslation(project, """{"shared":"Shared name"}""");
        var author = project.Services.GetRequiredService<CharacterAuthoringService>();
        using var vm = project.Services.GetRequiredService<CharactersPanelViewModel>();
        vm.Selected = vm.Rows.Single(row => row.Id == "first"); vm.DraftKey = "first_draft";
        vm.Selected = vm.Rows.Single(row => row.Id == "second"); Assert.That(vm.DraftKey, Is.EqualTo("second")); vm.DraftKey = "second_draft";
        vm.Selected = vm.Rows.Single(row => row.Id == "first"); Assert.That(vm.DraftKey, Is.EqualTo("first_draft"));
        await author.DeleteAsync(author.Rows().Single(row => row.Id == "first"));
        Assert.That(vm.Selected, Is.Null, "A shared name-text key cannot identify the remaining character as the removed selection.");
        vm.Selected = vm.Rows.Single(); Assert.That(vm.DraftKey, Is.EqualTo("second_draft"));
    });

    [Test]
    public Task CharacterApplyCompletionCannotDiscardAnotherSelectionOrAnUnrelatedNewDraft() => WithLocalizationProject(async project =>
    {
        var author = project.Services.GetRequiredService<CharacterAuthoringService>();
        var first = await author.CreateAsync("First", "first"); var second = await author.CreateAsync("Second", "second");
        using var vm = project.Services.GetRequiredService<CharactersPanelViewModel>();
        vm.Selected = vm.Rows.Single(row => row.Id == second.Id); vm.DraftName = "Pending second";
        vm.Selected = vm.Rows.Single(row => row.Id == first.Id); vm.DraftName = "Applied first";
        var started = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var held = history.ExecuteAsync(new CallbackEdit("Hold character apply", async () => { started.SetResult(); await release.Task; }, () => ValueTask.CompletedTask));
        await started.Task;
        var apply = vm.ApplyCommand.ExecuteAsync(null);
        try { vm.Selected = vm.Rows.Single(row => row.Id == second.Id); }
        finally { release.SetResult(); }
        await held; await apply;
        Assert.That(author.Rows().Single(row => row.Id == first.Id).Name, Is.EqualTo("Applied first"));
        Assert.That(vm.Selected?.Id, Is.EqualTo(second.Id)); Assert.That(vm.DraftName, Is.EqualTo("Pending second"));
        vm.AddCommand.Execute(null); vm.DraftName = "Pending new character";
        vm.Selected = vm.Rows.Single(row => row.Id == first.Id); vm.DraftName = "Applied again";
        await vm.ApplyCommand.ExecuteAsync(null); vm.AddCommand.Execute(null);
        Assert.That(vm.DraftName, Is.EqualTo("Pending new character"));
        vm.Selected = vm.Rows.Single(row => row.Id == first.Id); vm.RevertCommand.Execute(null); vm.AddCommand.Execute(null);
        Assert.That(vm.DraftName, Is.EqualTo("Pending new character"), "Reverting an existing character must not discard a separate new-character draft.");
        vm.Selected = vm.Rows.Single(row => row.Id == first.Id); vm.DraftName = "Applied after close";
        started = new(); release = new();
        held = history.ExecuteAsync(new CallbackEdit("Hold across close", async () => { started.SetResult(); await release.Task; }, () => ValueTask.CompletedTask));
        await started.Task; apply = vm.ApplyCommand.ExecuteAsync(null); vm.Dispose();
        try
        {
            using var reopened = project.Services.GetRequiredService<CharactersPanelViewModel>(); reopened.DraftName = "Newer input";
        }
        finally { release.SetResult(); }
        await held; await apply;
        using var latest = project.Services.GetRequiredService<CharactersPanelViewModel>();
        Assert.That(author.Rows().Single(row => row.Id == first.Id).Name, Is.EqualTo("Applied after close"));
        Assert.That(latest.DraftName, Is.EqualTo("Newer input"), "An old view's completion must not discard a newer view's draft.");
    });

    [Test]
    public Task LocalizationFiltersKeepMissingEmptyAndDraftIdentityDistinct() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); WriteTranslation(project, """{"line":"Source"}""");
        var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US");
        var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group); model.ExchangeLanguage = "en-US";
        var cell = model.Rows.Single().Cells.Single(cell => cell.Culture == "en-US");
        model.FilterIndex = 1; Assert.That(model.Rows, Has.Count.EqualTo(1));
        await cell.CommitAsync(true); Assert.That(cell.IsMissing, Is.False); Assert.That(model.Rows, Is.Empty, "Explicit empty translation is not missing.");
        model.FilterIndex = 0; cell.Text = "Unapplied translation";
        model.FilterIndex = 3; Assert.That(model.Rows.Single().Cells, Does.Contain(cell));
        model.Search = "no matching source"; Assert.That(model.Rows, Is.Empty);
        model.Search = "Unapplied"; Assert.That(model.Rows.Single().Cells, Does.Contain(cell));
        model.Selected = model.Categories.First(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
        Assert.That(model.Rows.Single().Cells.Single(c => c.Culture == "en-US"), Is.SameAs(cell));
        Assert.That(cell.Text, Is.EqualTo("Unapplied translation")); cell.Revert(); Assert.That(model.Rows, Is.Empty);
        model.Search = ""; model.FilterIndex = 0;
    });

    [Test]
    public async Task BranchPrimaryInputsStayAlignedWhenSemanticHintsOrErrorsAppear()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 300, 0); workspace.SelectNode(branch);
                var view = new NodeInspectorControl { DataContext = project.Services.GetRequiredService<NodeInspectorControlViewModel>() };
                var window = new Window { Content = view, Width = 800, Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    void Check()
                    {
                        window.UpdateLayout();
                        var text = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "GameTextInput");
                        var condition = view.GetVisualDescendants().OfType<ConditionEditorControl>().Single().FindControl<TextBox>("EditorBox")!;
                        var textCenter = text.TranslatePoint(new Point(0, text.Bounds.Height / 2), window)!.Value.Y;
                        var conditionCenter = condition.TranslatePoint(new Point(0, condition.Bounds.Height / 2), window)!.Value.Y;
                        Assert.That(textCenter, Is.EqualTo(conditionCenter).Within(1)); Assert.That(text.Bounds.Width, Is.GreaterThan(50));
                    }
                    Check(); branch.Source.Options![0].Text = "missing_text";
                    project.Services.GetRequiredService<IProjectContentSession>().Refresh(); Dispatcher.UIThread.RunJobs(); Check();
                    var condition = view.GetVisualDescendants().OfType<ConditionEditorControl>().Single();
                    condition.Text = "("; condition.FindControl<TextBox>("EditorBox")!.Focus(); view.Focus(); Dispatcher.UIThread.RunJobs(); Check();
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task AuthoringPanelsMountNarrowAndLocalizationKeepsFrozenContextWhileScrolling()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var item = CharacterEntry(project); WriteTranslation(project, """{"line":"A longer source text for the localization workspace."}""");
                var service = project.Services.GetRequiredService<GameLocalizationPanelService>(); await service.AddLanguageAsync("en-US"); await service.AddLanguageAsync("ja-JP");
                var model = project.Services.GetRequiredService<GameLocalizationPanelViewModel>(); model.Selected = model.Categories.Single(category => category.Id == item.Group);
                var view = new GameLocalizationPanelView { DataContext = model };
                var window = new Window { Content = view, Width = 900, Height = 650 }; window.Show(); Dispatcher.UIThread.RunJobs();
                void Capture(string name)
                {
                    if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is not { Length: > 0 } artifacts) return;
                    using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, name + ".png"));
                }
                try
                {
                    var header = view.FindControl<Border>("ContextHeader")!; var scroll = view.FindControl<ScrollViewer>("LocalizationTableScroll")!;
                    var left = header.TranslatePoint(default, window)!.Value.X;
                    scroll.Offset = new Vector(220, 0); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.That(scroll.Offset.X, Is.GreaterThan(0));
                    Assert.That(header.TranslatePoint(default, window)!.Value.X, Is.EqualTo(left).Within(1));
                    var sourceInput = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "LocalizationCellInput" && box.DataContext is LocalizationGridCell cell && cell.Culture == "zh-CN");
                    sourceInput.Focus(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.That(sourceInput.TranslatePoint(default, scroll)!.Value.X, Is.GreaterThanOrEqualTo(188), "Keyboard focus must reveal the cell beside the frozen context.");
                    scroll.Offset = new Vector(220, 0); Dispatcher.UIThread.RunJobs();
                    var rowContext = view.GetVisualDescendants().OfType<Button>().Single(button => button.Content is PathIcon icon && button.GetVisualAncestors().Any(ancestor => ancestor is ListBox list && list.Name == "LocalizationRows"));
                    Assert.That(rowContext.TranslatePoint(default, window)!.Value.X, Is.GreaterThanOrEqualTo(left));
                    Capture("localization-wide"); window.Width = 440; Dispatcher.UIThread.RunJobs();
                    Assert.That(view.FindControl<ComboBox>("CompactLocalizationCategory")!.IsVisible, Is.True); Capture("localization-narrow");
                    using var variables = project.Services.GetRequiredService<VariablesPanelViewModel>();
                    var variableView = new VariablesPanelView { DataContext = variables }; window.Content = variableView; Dispatcher.UIThread.RunJobs();
                    Click(window, variableView.FindControl<Button>("AddVariableButton")!); Dispatcher.UIThread.RunJobs();
                    Assert.That(variables.HasSelection, Is.True); Assert.That(project.Services.GetRequiredService<VariableAuthoringService>().Get(VariableScope.Player), Has.Count.EqualTo(1));
                    Assert.That((variables.HasChanges, variables.DraftDefault), Is.EqualTo((false, "")), "An untouched variable must not acquire a draft from hidden input controls.");
                    Assert.That(variableView.FindControl<TextBox>("NameInput")!.Bounds.Width, Is.GreaterThan(150)); Capture("variables-narrow");
                    window.Width = 900; Dispatcher.UIThread.RunJobs(); Capture("variables-wide");
                    var author = project.Services.GetRequiredService<CharacterAuthoringService>(); await author.CreateAsync("Character name", "character_key");
                    using var characters = project.Services.GetRequiredService<CharactersPanelViewModel>(); characters.Selected = characters.Rows.Single();
                    window.Content = new CharactersPanelView { DataContext = characters }; window.Width = 360; Dispatcher.UIThread.RunJobs(); Capture("characters-narrow");
                    await CreateVariantImages(project.RootPath, project.Services.GetRequiredService<ProjectFileAuthoringService>());
                    var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                    workspace.SelectedAsset = project.Services.GetRequiredService<GalNet.Editor.Services.Interfaces.IAssetCatalogService>().GetEntry("alice/idle_coat.png");
                    using var inspector = project.Services.GetRequiredService<AssetInspectorControlViewModel>();
                    var assetView = new AssetInspectorControl { DataContext = inspector }; window.Content = assetView; window.Height = 650; Dispatcher.UIThread.RunJobs();
                    await PumpCharacterUi(() => inspector.ImagePreview is not null);
                    var properties = assetView.FindControl<ScrollViewer>("AssetProperties")!; var preview = assetView.FindControl<Expander>("AssetPreview")!;
                    Assert.That(inspector.HasPreview, Is.True);
                    Assert.That(preview.TranslatePoint(default, window)!.Value.Y, Is.GreaterThanOrEqualTo(properties.Bounds.Bottom));
                    Capture("inspector-preview-bottom");
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }
}
