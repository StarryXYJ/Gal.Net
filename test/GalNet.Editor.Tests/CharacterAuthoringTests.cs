using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static (string Group, EditorEntryData Entry) CharacterEntry(GalProject project, string speaker = "")
    {
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var group = documents.Groups.First();
        var entry = new EditorEntryData
        {
            StableId = Guid.NewGuid().ToString("N"),
            Type = BuiltinEntryIds.Text,
            Parameters = new() { ["speaker"] = JsonSerializer.SerializeToElement(speaker), ["content"] = JsonSerializer.SerializeToElement("line") }
        };
        group.Value.Entries.Add(entry);
        return (group.Key, entry);
    }
    private static Task SetSpeaker(GalProject project, (string Group, EditorEntryData Entry) item, string speaker, CancellationToken token = default)
    {
        var parameters = new Dictionary<string, JsonElement>(item.Entry.Parameters) { ["speaker"] = JsonSerializer.SerializeToElement(speaker) };
        return project.Services.GetRequiredService<EntryFieldAuthoringService>().SetParametersAsync(item.Group, item.Entry, item.Entry.Type, parameters, token);
    }
    private static string Speaker(EditorEntryData entry) => entry.Parameters["speaker"].GetString()!;

    [Test]
    public Task CharacterInputCreatesRoleAndReferenceAsOneUndoAndSurvivesSaveReopen() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); var characters = project.Services.GetRequiredService<CharacterAuthoringService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var content = project.Services.GetRequiredService<IProjectContentSession>();
        history.Clear(); await SetSpeaker(project, item, "小明");
        var row = characters.Rows().Single(); Assert.That(row.Name, Is.EqualTo("小明")); Assert.That(row.Uses, Is.EqualTo(1));
        Assert.That(Speaker(item.Entry), Is.EqualTo(row.Id));
        await content.SaveAsync(); await history.UndoAsync();
        Assert.That(characters.Rows(), Is.Empty); Assert.That(Speaker(item.Entry), Is.Empty); Assert.That(history.CanUndo, Is.False);
        Assert.That(File.Exists(Path.Combine(project.RootPath, LocalizationJson.CatalogPath)), Is.True, "Undo retains the saved catalog baseline.");
        await content.SaveAsync(); await history.RedoAsync(); await content.SaveAsync();
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath); var restored = reopened.Services.GetRequiredService<CharacterAuthoringService>().Rows().Single();
        Assert.That(restored.Id, Is.EqualTo(row.Id)); Assert.That(restored.Name, Is.EqualTo("小明")); Assert.That(restored.Uses, Is.EqualTo(1));
        var next = await reopened.Services.GetRequiredService<CharacterAuthoringService>().CreateAsync("小红"); Assert.That(next.Id, Is.Not.EqualTo(row.Id));
    });

    [Test]
    public Task CharactersAllowSameNamesRejectDuplicateKeysAndResolveExactKeysBeforeNames() => WithLocalizationProject(async project =>
    {
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); var first = await characters.CreateAsync("同名", "hero");
        await characters.CreateAsync("同名", "other"); var special = await characters.CreateAsync("hero", "third");
        Assert.ThrowsAsync<EditorOperationException>(async () => await characters.CreateAsync("不同", "hero"));
        var item = CharacterEntry(project); var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        Assert.ThrowsAsync<EditorOperationException>(async () => await SetSpeaker(project, item, "同名"));
        Assert.That(history.CanUndo, Is.False); Assert.That(Speaker(item.Entry), Is.Empty);
        await SetSpeaker(project, item, "hero"); Assert.That(Speaker(item.Entry), Is.EqualTo(first.Id));
        await SetSpeaker(project, item, special.Id); Assert.That(Speaker(item.Entry), Is.EqualTo(special.Id));
        await SetSpeaker(project, item, ""); Assert.That(characters.Rows(), Has.Count.EqualTo(3));
    });

    [Test]
    public Task CharacterKeyRenameUpdatesUnopenedGroupsAndKeepsNameTranslations() => WithLocalizationProject(async project =>
    {
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); var row = await characters.CreateAsync("小明", "old");
        WriteTranslation(project, JsonSerializer.Serialize(new Dictionary<string, string> { [row.DisplayNameKey] = "Ming" }), "I18n/en-US/characters.json");
        var item = CharacterEntry(project, row.Id); var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var other = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 400, 100);
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var second = new EditorEntryData { StableId = Guid.NewGuid().ToString("N"), Type = item.Entry.Type, Parameters = new(item.Entry.Parameters) };
        documents.Groups[other.Id].Entries.Add(second);
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await characters.UpdateAsync(characters.Rows().Single(), "new", "明");
        Assert.That(Speaker(item.Entry), Is.EqualTo("new")); Assert.That(Speaker(second), Is.EqualTo("new"));
        var changed = characters.Rows().Single(); Assert.That(changed.Uses, Is.EqualTo(2)); Assert.That(changed.DisplayNameKey, Is.EqualTo(row.DisplayNameKey));
        Assert.That(project.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText(row.DisplayNameKey, "en-US").Text, Is.EqualTo("Ming"));
        Assert.ThrowsAsync<EditorOperationException>(async () => await characters.DeleteAsync(changed));
        await history.UndoAsync(); Assert.That(Speaker(second), Is.EqualTo("old")); Assert.That(characters.Rows().Single().Name, Is.EqualTo("小明"));
        Assert.That(history.CanUndo, Is.False); await history.RedoAsync();
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
    });

    [Test]
    public Task CharacterDeleteIsExplicitUndoableAndNumbersAreNeverRecycled() => WithLocalizationProject(async project =>
    {
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var first = await characters.CreateAsync("一"); await history.UndoAsync();
        var second = await characters.CreateAsync("二"); Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        history.Clear(); await characters.DeleteAsync(second); Assert.That(characters.Rows(), Is.Empty);
        await history.UndoAsync(); Assert.That(characters.Rows().Single().Id, Is.EqualTo(second.Id));
        await history.RedoAsync(); var third = await characters.CreateAsync("三"); Assert.That(third.Id, Is.Not.EqualTo(second.Id));
    });

    [Test]
    public Task UnknownCharacterIdsRemainErrorsAndUncertainReferencesBlockDestructiveEdits() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project, "旧姓名"); var characters = project.Services.GetRequiredService<CharacterAuthoringService>();
        Assert.That(characters.Rows(), Is.Empty);
        var row = await characters.CreateAsync("旧姓名", "known"); Assert.That(Speaker(item.Entry), Is.EqualTo("旧姓名"));
        Assert.That(project.Services.GetRequiredService<ProjectLocalizationQuery>().Get()!.Issues.Any(issue => issue.Code == "character.unknown"), Is.True);
        await SetSpeaker(project, item, row.Id); Assert.That(Speaker(item.Entry), Is.EqualTo(row.Id));
        var unused = await characters.CreateAsync("未用");
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        documents.Groups[item.Group].Entries.Add(new()
        {
            StableId = Guid.NewGuid().ToString("N"),
            Type = "unknown.schema",
            Parameters = new() { ["opaque"] = JsonSerializer.SerializeToElement("potential-character-reference") }
        });
        Assert.That(characters.Complete, Is.False);
        Assert.ThrowsAsync<EditorOperationException>(async () => await characters.DeleteAsync(unused));
        Assert.ThrowsAsync<EditorOperationException>(async () => await characters.UpdateAsync(row, "renamed", row.Name!));
        await characters.UpdateAsync(row, row.Id, "新姓名"); Assert.That(characters.Rows().Single(r => r.Id == row.Id).Name, Is.EqualTo("新姓名"));
    });

    [Test]
    public Task CharacterCommitsSerializeReuseNamesAndRejectCanceledOrObsoleteEntries() => WithLocalizationProject(async project =>
    {
        var first = CharacterEntry(project); var second = CharacterEntry(project);
        await Task.WhenAll(SetSpeaker(project, first, "共享"), SetSpeaker(project, second, "共享"));
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); Assert.That(characters.Rows(), Has.Count.EqualTo(1));
        Assert.That(Speaker(first.Entry), Is.EqualTo(Speaker(second.Entry)));
        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await SetSpeaker(project, first, "取消", new CancellationToken(true)));
        project.Services.GetRequiredService<IEditorDocumentService>().Groups[first.Group].Entries.Remove(first.Entry);
        Assert.ThrowsAsync<EditorOperationException>(async () => await SetSpeaker(project, first, "过期"));
        Assert.That(characters.Rows(), Has.Count.EqualTo(1)); Assert.That(characters.Rows().Single().Uses, Is.EqualTo(1));
    });

    [Test]
    public Task CharacterRelatedInvalidDraftAndStaleRenamePreserveCurrentValues() => WithLocalizationProject(async project =>
    {
        var characters = project.Services.GetRequiredService<CharacterAuthoringService>(); var row = await characters.CreateAsync("原名", "hero");
        var item = CharacterEntry(project); var texts = project.Services.GetRequiredService<ProjectTextFiles>();
        var buffer = texts.Open("I18n/zh-CN/characters.json"); buffer.Text = "{broken";
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        Assert.ThrowsAsync(Is.InstanceOf<JsonException>(), async () => await SetSpeaker(project, item, "新角色"));
        Assert.That(Speaker(item.Entry), Is.Empty); Assert.That(characters.Rows(), Has.Count.EqualTo(1)); Assert.That(buffer.Text, Is.EqualTo("{broken")); Assert.That(history.CanUndo, Is.False);
        buffer.CancelCommand.Execute(null); await characters.UpdateAsync(row, row.Id, "已改名");
        Assert.ThrowsAsync<EditorOperationException>(async () => await characters.UpdateAsync(row, "stale", "旧草稿"));
        Assert.That(characters.Rows().Single().Id, Is.EqualTo(row.Id)); Assert.That(characters.Rows().Single().Name, Is.EqualTo("已改名"));
    });

    [Test]
    public async Task CharacterPanelMountedControlsCreateRenameFilterAndDisposeDock()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            await WithLocalizationProject(async project =>
            {
                using var vm = project.Services.GetRequiredService<CharactersPanelViewModel>(); var author = project.Services.GetRequiredService<CharacterAuthoringService>();
                var view = new CharactersPanelView { DataContext = vm }; var window = new Window { Content = view, Width = 900, Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    Click(window, view.FindControl<Button>("AddCharacter")!); view.FindControl<TextBox>("CharacterName")!.Text = "小明";
                    await vm.ApplyCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs(); Assert.That(author.Rows().Single().Name, Is.EqualTo("小明"));
                    view.FindControl<TextBox>("CharacterKey")!.Text = "hero"; await vm.ApplyCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                    Assert.That(author.Rows().Single().Id, Is.EqualTo("hero"));
                    view.FindControl<TextBox>("CharacterSearch")!.Text = "HERO"; Dispatcher.UIThread.RunJobs(); Assert.That(vm.Rows, Has.Count.EqualTo(1));
                    var locale = project.Services.GetRequiredService<IEditorLocalizationService>(); vm.DraftName = "未提交"; locale.ApplyLocale("en-US"); Assert.That(vm.DraftName, Is.EqualTo("未提交"));
                    vm.RevertCommand.Execute(null); Assert.That(vm.DraftName, Is.EqualTo("小明"));
                    var dock = project.Services.GetRequiredService<EditorDockFactory>(); var layout = dock.CreateLayout(); dock.InitLayout(layout);
                    dock.OpenPanel(EditorDockPanelIds.Characters); dock.OpenPanel(EditorDockPanelIds.Characters); Assert.That(dock.HasGlobalPanel(EditorDockPanelIds.Characters), Is.True);
                    dock.ToggleGlobalPanel(EditorDockPanelIds.Characters); Assert.That(dock.HasGlobalPanel(EditorDockPanelIds.Characters), Is.False);
                    vm.Dispose(); vm.AddCommand.Execute(null); await vm.ApplyCommand.ExecuteAsync(null); Assert.That(author.Rows(), Has.Count.EqualTo(1));
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CharacterSpeakerMountedFocusLossSearchSelectionAndComposition()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Text);
                using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
                var view = new EntryListEditorControl { DataContext = vm }; var outside = new Button { Content = "outside" };
                var panel = new StackPanel { Children = { view, outside } }; var window = new Window { Content = panel, Width = 1100, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput");
                    var author = project.Services.GetRequiredService<CharacterAuthoringService>();
                    input.Focus(); input.Text = "小明"; Dispatcher.UIThread.RunJobs(); outside.Focus();
                    await PumpCharacterUi(() => author.Rows().Count == 1); Assert.That(author.Rows().Single().Name, Is.EqualTo("小明"));
                    await PumpCharacterUi(() => view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput").Text == "小明");
                    var other = await author.CreateAsync("小红", "red"); Dispatcher.UIThread.RunJobs();
                    input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput");
                    input.Focus(); input.Text = "RED"; Dispatcher.UIThread.RunJobs();
                    var popup = input.Parent!.GetLogicalChildren().OfType<Popup>().Single(); var list = (ListBox)((Border)popup.Child!).Child!;
                    Assert.That(list.Items.Cast<CharacterRow>().Single().Id, Is.EqualTo(other.Id));
                    PressGroupKey(input, Key.Enter);
                    await PumpCharacterUi(() => group.Entries[0].Parameters.GetValueOrDefault("speaker") == "red"); Assert.That(author.Rows(), Has.Count.EqualTo(2));
                    input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput");
                    input.Focus(); input.Text = "RED"; Dispatcher.UIThread.RunJobs();
                    popup = input.Parent!.GetLogicalChildren().OfType<Popup>().Single(); list = (ListBox)((Border)popup.Child!).Child!;
                    PressGroupKey(input, Key.Down); PressGroupKey(list, Key.Enter);
                    Assert.That(group.Entries[0].Parameters.GetValueOrDefault("speaker"), Is.EqualTo("red"));
                    input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput"); input.Focus(); input.Text = "小明"; Dispatcher.UIThread.RunJobs();
                    popup = input.Parent!.GetLogicalChildren().OfType<Popup>().Single(); list = (ListBox)((Border)popup.Child!).Child!;
                    list.UpdateLayout(); var choice = list.GetVisualDescendants().OfType<ListBoxItem>().Single();
                    var choiceRoot = TopLevel.GetTopLevel(choice)!; var point = choice.TranslatePoint(new Point(20, choice.Bounds.Height / 2), choiceRoot)!.Value;
                    choiceRoot.MouseDown(point, MouseButton.Left); choiceRoot.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    await PumpCharacterUi(() => group.Entries[0].Parameters.GetValueOrDefault("speaker") == author.Rows().Single(row => row.Name == "小明").Id);
                    Assert.That(author.Rows(), Has.Count.EqualTo(2), "A candidate click must not first create its search text as a new character.");
                    var beforeComposition = group.Entries[0].Parameters.GetValueOrDefault("speaker");
                    input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput"); input.Focus(); input.Text = "RED";
                    var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single(); presenter.PreeditText = "拼";
                    PressGroupKey(input, Key.Enter); Assert.That(author.Rows(), Has.Count.EqualTo(2));
                    Assert.That(group.Entries[0].Parameters.GetValueOrDefault("speaker"), Is.EqualTo(beforeComposition), "IME Enter cannot accept the highlighted candidate.");
                    presenter.PreeditText = null; PressGroupKey(input, Key.Escape); outside.Focus(); Dispatcher.UIThread.RunJobs();
                    Assert.That(author.Rows(), Has.Count.EqualTo(2));
                    input.Focus(); input.Text = "   "; outside.Focus(); await PumpCharacterUi(() => group.Entries[0].Parameters.GetValueOrDefault("speaker") == "");
                    Assert.That(author.Rows(), Has.Count.EqualTo(2));
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }
    private static async Task PumpCharacterUi(Func<bool> ready)
    {
        for (var i = 0; i < 100; i++) { Dispatcher.UIThread.RunJobs(); if (ready()) return; await Task.Delay(10); }
        Assert.Fail("Character UI did not finish its commit.");
    }

    [Test]
    public async Task CharacterNestedListFieldUsesSemanticEditorAndRenameUpdatesStoredWirePath()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            var fields = new DynamicParameterTable([new(new FieldDescriptor("items", new(typeof(List<CharacterNestedDto>))))]);
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules().Append(new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.roles", fields)])));
            await WithLocalizationProject(async project =>
            {
                var item = CharacterEntry(project); item.Entry.Type = "custom.roles";
                item.Entry.Parameters = new() { ["items"] = JsonSerializer.SerializeToElement(new[] { new CharacterNestedDto { Speaker = "" } }) };
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, item.Group);
                var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1100, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    foreach (var expander in view.GetVisualDescendants().OfType<StructuredFieldEditor>().ToArray()) expander.IsExpanded = true;
                    Dispatcher.UIThread.RunJobs();
                    var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CharacterInput");
                    input.Focus(); input.Text = "嵌套角色"; Dispatcher.UIThread.RunJobs(); PressGroupKey(input, Key.Enter);
                    var characters = project.Services.GetRequiredService<CharacterAuthoringService>();
                    await PumpCharacterUi(() => characters.Rows().Count == 1);
                    var row = characters.Rows().Single(); Assert.That(item.Entry.Parameters["items"][0].GetProperty("Speaker").GetString(), Is.EqualTo(row.Id));
                    Assert.That(row.Uses, Is.EqualTo(1));
                    await characters.UpdateAsync(row, "nested", row.Name!); Assert.That(item.Entry.Parameters["items"][0].GetProperty("Speaker").GetString(), Is.EqualTo("nested"));
                    await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync(); Assert.That(item.Entry.Parameters["items"][0].GetProperty("Speaker").GetString(), Is.EqualTo(row.Id));
                }
                finally { window.Close(); }
            }, catalog: catalog); return true;
        }, CancellationToken.None);
    }
    public sealed class CharacterNestedDto : IFieldDto
    {
        [Field(0), CharacterReference] public string Speaker { get; set; } = "";
        public static IReadOnlyList<FieldDescriptor> Schema { get; } = [new("speaker", new(typeof(string)), semantic: FieldValueSemantic.CharacterReference)];
        [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => Speaker;
        public void WriteField(string id, object? value) => Speaker = (string)value!;
    }
}
