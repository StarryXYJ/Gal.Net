using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task VariablePanelRenamesOnlyKnownReferencesWithOneHistoryAndPreservesDescriptionsOnReopen()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "VariableReferences"), "Variables", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var author = project.Services.GetRequiredService<VariableAuthoringService>();
            var variable = author.Add(VariableScope.Save); var uid = variable.DefaultValue.Uid;
            author.Update(VariableScope.Save, uid, "score", VariableType.Int, "3", "Score description");
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var text = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Text).Single(); text.Parameters = new() { ["content"] = "score is text, not a reference" };
            var set = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.SetVariable).Single();
            set.Parameters = new() { ["target"] = "save.score", ["expression"] = "[score] + [save.score] + score + 1" }; set.Condition = "[score] > 1";
            var unopened = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 200);
            var other = workspace.InsertEntriesInto(unopened, 0, 1, BuiltinEntryIds.Wait).Single(); other.Condition = "[score] > 2";
            var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 700, 200); branch.Options[0].Condition = "[score] > 0";
            using var groupPage = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var view = new EntryListEditorControl { DataContext = groupPage }; var window = new Window { Content = view, Width = 1100, Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                var condition = view.GetVisualDescendants().OfType<ConditionEditorControl>().Single(control => control.Text == "[score] > 1");
                set.Condition = "("; Dispatcher.UIThread.RunJobs(); condition.FindControl<TextBox>("EditorBox")!.Focus(); view.Focus();
                Assert.That(condition.FindControl<Border>("ValidationBorder")!.IsVisible, Is.True);
                set.Condition = "[score] > 1"; Dispatcher.UIThread.RunJobs();
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                Assert.That(author.Inspect(VariableScope.Save, uid).Count(item => item.Certain), Is.EqualTo(5));
                var warnings = author.Update(VariableScope.Save, uid, "points", VariableType.Int, "9", "Updated description");
                Assert.That(warnings, Has.Count.EqualTo(1)); Assert.That(text.Parameters["content"], Does.Contain("score"));
                Assert.That(set.Parameters["target"], Is.EqualTo("save.points")); Assert.That(set.Parameters["expression"], Is.EqualTo("[points] + [save.points] + points + 1")); Assert.That(set.Condition, Is.EqualTo("[points] > 1")); Assert.That(other.Condition, Is.EqualTo("[points] > 2")); Assert.That(branch.Options[0].Condition, Is.EqualTo("[points] > 0"));
                Assert.That(groupPage.ConditionSuggestions.Any(item => item.Name == "points"), Is.True);
                Assert.That(view.GetVisualDescendants().OfType<ConditionEditorControl>().Any(control => control.Text == "[points] > 1"), Is.True);
                Assert.That(condition.FindControl<Border>("ValidationBorder")!.IsVisible, Is.False, "Variable changes refresh already displayed diagnostics.");
                Assert.Throws<EditorOperationException>(() => author.Delete(VariableScope.Save, uid));
                await history.UndoAsync(); Assert.That(author.Get(VariableScope.Save).Single().Name, Is.EqualTo("score")); Assert.That(author.Get(VariableScope.Save).Single().DefaultValue.AsInt(), Is.EqualTo(3)); Assert.That(set.Parameters["target"], Is.EqualTo("save.score")); Assert.That(history.CanUndo, Is.False, "No legacy listener should create a second history entry.");
                await history.RedoAsync(); Assert.That(author.Get(VariableScope.Save).Single().DefaultValue.Uid, Is.EqualTo(uid));
                await workspace.SaveAsync(); var path = project.RootPath;
                window.Close(); groupPage.Dispose(); await projects.CloseAsync();
                var reopened = await projects.OpenAsync(path); var declaration = reopened.Services.GetRequiredService<VariableAuthoringService>().Get(VariableScope.Save).Single();
                Assert.That(declaration.Description, Is.EqualTo("Updated description")); Assert.That(declaration.DefaultValue.Uid, Is.EqualTo(uid));
                Assert.That(reopened.Settings.SaveVariables.Single().Description, Is.EqualTo("Updated description"));
                await projects.CloseAsync();
            }
            finally { window.Close(); groupPage.Dispose(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task VariableExpressionsKeepQuotedFunctionsAndUncertainTextAndDeleteOnlyWhenUnreferenced()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "VariableLexing"), "Variables", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var author = project.Services.GetRequiredService<VariableAuthoringService>();
        var uid = author.Add(VariableScope.Player).DefaultValue.Uid; author.Update(VariableScope.Player, uid, "score", VariableType.String, "hello", "");
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.SetVariable).Single();
        entry.Condition = "'score [score]' == \"score\" && score(1) > 0";
        entry.Parameters = new() { ["target"] = "other", ["expression"] = "[score] + '\\'score' + \"[score]\"" };
        var bad = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Wait).Single(); bad.Condition = "[score] + 'unterminated";
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        var warnings = author.Update(VariableScope.Player, uid, "result", VariableType.String, "new", "");
        Assert.That(entry.Condition, Is.EqualTo("'score [score]' == \"score\" && score(1) > 0"));
        Assert.That(entry.Parameters["expression"], Is.EqualTo("[result] + '\\'score' + \"[score]\""));
        Assert.That(bad.Condition, Does.Contain("score")); Assert.That(warnings.Count, Is.EqualTo(2));
        Assert.Throws<EditorOperationException>(() => author.Delete(VariableScope.Player, uid));
        entry.Parameters = new() { ["target"] = "other", ["expression"] = "'result'" }; history.Clear();
        author.Delete(VariableScope.Player, uid); Assert.That(author.Get(VariableScope.Player), Is.Empty);
        await history.UndoAsync(); Assert.That(author.Get(VariableScope.Player).Single().DefaultValue.Uid, Is.EqualTo(uid)); Assert.That(history.CanUndo, Is.False);
        await projects.CloseAsync();
    }

    [Test]
    public async Task VariablesRejectInvalidNamesDefaultsAndStaleDraftAndCompensateObserverFailure()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "VariableFailures"), "Variables", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var author = project.Services.GetRequiredService<VariableAuthoringService>(); var defs = project.Services.GetRequiredService<IVariableDefinitionService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var uid = author.Add(VariableScope.Player).DefaultValue.Uid; author.Update(VariableScope.Player, uid, "playerFlag", VariableType.Bool, "true", "");
        var other = author.Add(VariableScope.Save).DefaultValue.Uid; history.Clear();
        foreach (var name in new[] { "playerFlag", "123", "bad name", "gallery_1_unlocked", "true" }) Assert.Throws<EditorOperationException>(() => author.Update(VariableScope.Save, other, name, VariableType.String, "", ""));
        foreach (var pair in new[] { (VariableType.Bool, "1"), (VariableType.Int, "2147483648"), (VariableType.Float, "NaN"), (VariableType.Float, "1,2") }) Assert.Throws<EditorOperationException>(() => author.Update(VariableScope.Save, other, "valid", pair.Item1, pair.Item2, ""));
        Assert.That(history.CanUndo, Is.False);
        var snapshot = JsonSerializer.Serialize(author.Get(VariableScope.Player).Single());
        author.Update(VariableScope.Player, uid, "playerFlag", VariableType.Float, "1.25", "float"); Assert.That(author.Get(VariableScope.Player).Single().DefaultValue.AsFloat(), Is.EqualTo(1.25f));
        Assert.Throws<EditorOperationException>(() => author.Update(VariableScope.Player, uid, "stale", VariableType.Int, "1", "", snapshot));
        await history.UndoAsync(); Assert.That(author.Get(VariableScope.Player).Single().DefaultValue.AsBool(), Is.True); Assert.That(history.CanUndo, Is.False);
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var reference = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single(); reference.Condition = "[playerFlag] == true"; history.Clear();
        var fail = true; void FailOnce(VariableScope _) { if (fail) { fail = false; throw new IOException("variable observer"); } }
        defs.DefinitionsChanged += FailOnce;
        try { Assert.Throws<IOException>(() => author.Update(VariableScope.Player, uid, "changed", VariableType.Int, "2", "")); Assert.That(author.Get(VariableScope.Player).Single().Name, Is.EqualTo("playerFlag")); Assert.That(reference.Condition, Is.EqualTo("[playerFlag] == true")); Assert.That(history.CanUndo, Is.False); }
        finally { defs.DefinitionsChanged -= FailOnce; }
        void FailAlways(VariableScope _) => throw new IOException("persistent observer"); defs.DefinitionsChanged += FailAlways;
        try { Assert.Throws<HistoryRepairRequiredException>(() => author.Delete(VariableScope.Save, other)); Assert.That(history.RepairRequired, Is.True); }
        finally { defs.DefinitionsChanged -= FailAlways; await projects.CloseAsync(); }
    }

    [Test]
    public async Task VariablesPanelMountedControlsCommitDraftSwitchScopesAndReleaseDockPage()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "VariablesUi"), "Variables", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var author = project.Services.GetRequiredService<VariableAuthoringService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
            using var vm = project.Services.GetRequiredService<VariablesPanelViewModel>(); var view = new VariablesPanelView { DataContext = vm }; var window = new Window { Content = view, Width = 1000, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                history.Clear(); Click(window, view.FindControl<Button>("AddVariableButton")!); Assert.That(vm.HasSelection, Is.True); Assert.That(author.Get(VariableScope.Player), Has.Count.EqualTo(1)); history.Clear();
                var name = view.FindControl<TextBox>("NameInput")!; name.Text = "unlockFlag"; var description = view.FindControl<TextBox>("DescriptionInput")!; description.Text = "Unlock description";
                view.FindControl<ComboBox>("TypeInput")!.SelectedItem = vm.Types.Single(item => item.Value == VariableType.Bool); Dispatcher.UIThread.RunJobs();
                view.FindControl<TextBox>("DefaultInput")!.Text = "true"; Dispatcher.UIThread.RunJobs(); Assert.That(author.Get(VariableScope.Player).Single().Name, Is.Not.EqualTo("unlockFlag"), "Draft typing must not mutate the author document.");
                Click(window, view.FindControl<Button>("ApplyVariableButton")!); Assert.That(author.Get(VariableScope.Player).Single().Name, Is.EqualTo("unlockFlag")); Assert.That(author.Get(VariableScope.Player).Single().DefaultValue.AsBool(), Is.True);
                await history.UndoAsync(); Assert.That(vm.DraftName, Is.Not.EqualTo("unlockFlag")); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); Assert.That(vm.DraftName, Is.EqualTo("unlockFlag"));
                var tabs = view.FindControl<TabControl>("ScopeTabs")!; tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); Click(window, view.FindControl<Button>("AddVariableButton")!); Assert.That(author.Get(VariableScope.Save), Has.Count.EqualTo(1)); Assert.That(vm.ScopeIndex, Is.EqualTo(1));
                vm.DraftName = "unlockFlag"; Click(window, view.FindControl<Button>("ApplyVariableButton")!); Assert.That(vm.MessageText, Is.Not.Empty);
                var localization = project.Services.GetRequiredService<IEditorLocalizationService>(); foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" }) { localization.ApplyLocale(locale); Assert.That(vm.MessageText, Is.EqualTo(localization["Error.variable.duplicate"])); }
                var draftName = vm.DraftName; var saveUid = vm.Selected!.Uid;
                author.Update(VariableScope.Save, saveUid, "external", VariableType.Int, "2", ""); Assert.That(vm.DraftName, Is.EqualTo(draftName), "Unrelated notifications must not discard a draft.");
                vm.ApplyCommand.Execute(null); Assert.That(vm.MessageText, Is.EqualTo(localization["Error.variable.changed"])); vm.RevertCommand.Execute(null); Assert.That(vm.DraftName, Is.EqualTo("external"));
                var dock = project.Services.GetRequiredService<EditorDockFactory>(); var layout = dock.CreateLayout(); dock.InitLayout(layout); dock.OpenPanel(EditorDockPanelIds.Variables); dock.OpenPanel(EditorDockPanelIds.Variables); Assert.That(dock.HasGlobalPanel(EditorDockPanelIds.Variables), Is.True); dock.ToggleGlobalPanel(EditorDockPanelIds.Variables); Assert.That(dock.HasGlobalPanel(EditorDockPanelIds.Variables), Is.False);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "variables-panel.png")); }
                vm.Dispose(); var count = author.Get(VariableScope.Save).Count; vm.AddVariableCommand.Execute(null); Assert.That(author.Get(VariableScope.Save).Count, Is.EqualTo(count));
            }
            finally { window.Close(); vm.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
