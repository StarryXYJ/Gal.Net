using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static async Task SetGameText(GalProject project, string group, EditorEntryData entry, string value, string property = "content")
    {
        var texts = project.Services.GetRequiredService<GameTextAuthoringService>();
        var key = entry.Parameters.TryGetValue(property, out var raw) && raw.ValueKind == JsonValueKind.String ? raw.GetString()! : "";
        var draft = texts.Register(texts.Capture(key, entry.StableId), value);
        try
        {
            var parameters = new Dictionary<string, JsonElement>(entry.Parameters) { [property] = JsonSerializer.SerializeToElement(draft.Token) };
            await project.Services.GetRequiredService<EntryFieldAuthoringService>().SetParametersAsync(group, entry, entry.Type, parameters, CancellationToken.None);
        }
        finally { texts.Release(draft); }
    }

    [Test]
    public Task GameTextFirstEditKeepsStableKeyAcrossMoveUndoAndReopenAndMarksTranslationsForReview() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); item.Entry.Parameters["content"] = JsonSerializer.SerializeToElement("");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await SetGameText(project, item.Group, item.Entry, "原文"); var key = item.Entry.Parameters["content"].GetString()!;
        Assert.That(key, Is.Not.Empty.And.Not.EqualTo("原文"));
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>(); Assert.That(store.CaptureText(key).Text, Is.EqualTo("原文"));
        await history.UndoAsync(); Assert.That(item.Entry.Parameters["content"].GetString(), Is.Empty); Assert.That(store.CaptureText(key).Text, Is.Null); Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync(); WriteTranslation(project, JsonSerializer.Serialize(new Dictionary<string, string> { [key] = "English" }), "I18n/en-US/lines.json");
        await SetGameText(project, item.Group, item.Entry, "修改原文");
        Assert.That(item.Entry.Parameters["content"].GetString(), Is.EqualTo(key)); Assert.That(store.CaptureText(key, "en-US").Text, Is.EqualTo("English"));
        Assert.That(LocalizationTextState.NeedsReview(store.CaptureFile(LocalizationJson.MetadataPath).Data, key, "en-US"), Is.True);
        await history.UndoAsync(); Assert.That(store.CaptureText(key).Text, Is.EqualTo("原文")); await history.RedoAsync();
        var other = CharacterEntry(project); var author = project.Services.GetRequiredService<GroupEntryAuthoringService>();
        author.Move(item.Group, [item.Entry.StableId], item.Entry.StableId, 1); Assert.That(item.Entry.Parameters["content"].GetString(), Is.EqualTo(key));
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync(); var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Services.GetRequiredService<LocalizationDocumentStore>().CaptureText(key).Text, Is.EqualTo("修改原文"));
        Assert.That(reopened.Services.GetRequiredService<IEditorDocumentService>().Groups[item.Group].Entries.Single(entry => entry.StableId == item.Entry.StableId).Parameters["content"].GetString(), Is.EqualTo(key));
    });

    [Test]
    public Task GameTextEntryCopyCapturesAllLanguagesAndSharesCharactersButHasIndependentText() => WithLocalizationProject(async project =>
    {
        var role = await project.Services.GetRequiredService<CharacterAuthoringService>().CreateAsync("小明", "hero");
        var item = CharacterEntry(project, role.Id); await SetGameText(project, item.Group, item.Entry, "复制前"); var original = item.Entry.Parameters["content"].GetString()!;
        WriteTranslation(project, JsonSerializer.Serialize(new Dictionary<string, string> { [original] = "English" }), "I18n/en-US/lines.json");
        var author = project.Services.GetRequiredService<GroupEntryAuthoringService>(); var clip = author.Copy(item.Group, [item.Entry.StableId]);
        await SetGameText(project, item.Group, item.Entry, "复制后改原文");
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        var copied = (await author.PasteAsync(item.Group, clip, null)).Single(); var key = copied.Source.Parameters["content"].GetString()!;
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>();
        Assert.That(key, Is.Not.EqualTo(original)); Assert.That(store.CaptureText(key).Text, Is.EqualTo("复制前")); Assert.That(store.CaptureText(key, "en-US").Text, Is.EqualTo("English"));
        Assert.That(copied.Source.Parameters["speaker"].GetString(), Is.EqualTo(role.Id));
        await history.UndoAsync(); Assert.That(store.CaptureText(key).Text, Is.Null); Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync(); Assert.That(copied.Source.Parameters["content"].GetString(), Is.EqualTo(key));
        await SetGameText(project, item.Group, copied.Source, "副本"); Assert.That(store.CaptureText(original).Text, Is.EqualTo("复制后改原文"));
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
    });

    [Test]
    public Task GameTextGraphCopyUndoSaveRedoKeepsSavedBaselineFreeOfUnsavedTextReferences() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); await SetGameText(project, item.Group, item.Entry, "正文");
        var graph = project.Services.GetRequiredService<GraphAuthoringService>(); var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var optionNode = await graph.AddNodeAsync(GraphNodeKind.ChoiceBranch, 300, 0); var option = documents.CurrentDocument.Nodes.Single(node => node.Id == optionNode).Options![0];
        var texts = project.Services.GetRequiredService<GameTextAuthoringService>(); var draft = texts.Register(texts.Capture(option.Text, option.Id), "选项");
        try { Assert.That(await texts.BranchBinding(optionNode, option).CommitAsync(new(JsonSerializer.SerializeToElement(draft.Token)), CancellationToken.None), Is.Null); }
        finally { texts.Release(draft); }
        var clip = graph.CopyNodes([item.Group, optionNode]); var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        var ids = await graph.PasteAsync(clip, 500, 200); var group = documents.CurrentDocument.Nodes.Single(node => ids.Contains(node.Id) && node.Type == "Group");
        var copiedEntry = documents.Groups[group.Id].Entries.Single(); var key = copiedEntry.Parameters["content"].GetString()!;
        var copiedOption = documents.CurrentDocument.Nodes.Single(node => ids.Contains(node.Id) && node.Type == "Branch").Options![0];
        Assert.That(copiedOption.Text, Is.Not.EqualTo(option.Text)); Assert.That(texts.Read(copiedOption.Text), Is.EqualTo("选项"));
        var content = project.Services.GetRequiredService<IProjectContentSession>(); await content.SaveAsync(); await history.UndoAsync(); await content.SaveAsync();
        await history.RedoAsync(); Assert.That(texts.Read(key), Is.EqualTo("正文"));
        Assert.That(content.CaptureSaved().GroupEntries[group.Id], Is.Empty, "Redo must establish a valid empty file baseline until Save All publishes text and entries together.");
        Assert.That(content.CaptureWorking().GroupEntries[group.Id].Single().Parameters["content"].GetString(), Is.EqualTo(key));
        await content.SaveAsync(); Assert.That(content.CaptureSaved().GroupEntries[group.Id].Single().Parameters["content"].GetString(), Is.EqualTo(key));
    });

    [Test]
    public Task GameTextDraftRejectsSourceConflictAndCancellationWithoutChangingEntry() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); await SetGameText(project, item.Group, item.Entry, "原文");
        var texts = project.Services.GetRequiredService<GameTextAuthoringService>(); var key = item.Entry.Parameters["content"].GetString()!;
        var draft = texts.Register(texts.Capture(key, item.Entry.StableId), "旧草稿");
        await SetGameText(project, item.Group, item.Entry, "新原文"); var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        var parameters = new Dictionary<string, JsonElement>(item.Entry.Parameters) { ["content"] = JsonSerializer.SerializeToElement(draft.Token) };
        try
        {
            Assert.ThrowsAsync<EditorOperationException>(async () => await project.Services.GetRequiredService<EntryFieldAuthoringService>().SetParametersAsync(item.Group, item.Entry, item.Entry.Type, parameters, CancellationToken.None));
            Assert.That(texts.Read(key), Is.EqualTo("新原文")); Assert.That(history.CanUndo, Is.False);
            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await project.Services.GetRequiredService<EntryFieldAuthoringService>().SetParametersAsync(item.Group, item.Entry, item.Entry.Type, parameters, new(true)));
        }
        finally { texts.Release(draft); }
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task GameTextUndoPreservesRoleNumberReservationsAndEmptyTextIsDistinctFromMissing(bool explicitNull) => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); item.Entry.Parameters.Remove("content");
        if (explicitNull) item.Entry.Parameters["content"] = JsonSerializer.SerializeToElement<object?>(null);
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        await SetGameText(project, item.Group, item.Entry, ""); var key = item.Entry.Parameters["content"].GetString()!;
        var store = project.Services.GetRequiredService<LocalizationDocumentStore>(); Assert.That(store.CaptureText(key).Text, Is.Empty);
        var roles = project.Services.GetRequiredService<CharacterAuthoringService>(); var first = await roles.CreateAsync("一");
        await history.UndoAsync(); await history.UndoAsync();
        Assert.That(item.Entry.Parameters.ContainsKey("content"), Is.EqualTo(explicitNull)); if (explicitNull) Assert.That(item.Entry.Parameters["content"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(store.CaptureText(key).Text, Is.Null); Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync(); Assert.That(store.CaptureText(key).Text, Is.Empty);
        var next = await roles.CreateAsync("二"); Assert.That(next.Id, Is.Not.EqualTo(first.Id));
    });

    [Test]
    public Task GameTextAndNewSpeakerInOneDtoSubmissionShareOneHistory() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); item.Entry.Parameters["content"] = JsonSerializer.SerializeToElement("");
        var texts = project.Services.GetRequiredService<GameTextAuthoringService>(); var draft = texts.Register(texts.Capture("", item.Entry.StableId), "正文");
        var parameters = new Dictionary<string, JsonElement>(item.Entry.Parameters) { ["speaker"] = JsonSerializer.SerializeToElement("新角色"), ["content"] = JsonSerializer.SerializeToElement(draft.Token) };
        var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
        try { await project.Services.GetRequiredService<EntryFieldAuthoringService>().SetParametersAsync(item.Group, item.Entry, item.Entry.Type, parameters, CancellationToken.None); }
        finally { texts.Release(draft); }
        var roles = project.Services.GetRequiredService<CharacterAuthoringService>(); Assert.That(roles.Rows().Single().Uses, Is.EqualTo(1));
        var key = item.Entry.Parameters["content"].GetString()!; Assert.That(texts.Read(key), Is.EqualTo("正文"));
        await history.UndoAsync(); Assert.That(roles.Rows(), Is.Empty); Assert.That(item.Entry.Parameters["speaker"].GetString(), Is.Empty); Assert.That(item.Entry.Parameters["content"].GetString(), Is.Empty); Assert.That(history.CanUndo, Is.False);
        await history.RedoAsync(); Assert.That(texts.Read(key), Is.EqualTo("正文")); Assert.That(roles.Rows(), Has.Count.EqualTo(1));
    });

    [Test]
    public Task GameTextClipboardAcrossProjectsCarriesDetachedLanguageSnapshots() => WithLocalizationProject(async project =>
    {
        var item = CharacterEntry(project); await SetGameText(project, item.Group, item.Entry, "跨项目原文"); var key = item.Entry.Parameters["content"].GetString()!;
        WriteTranslation(project, JsonSerializer.Serialize(new Dictionary<string, string> { [key] = "Cross-project" }), "I18n/en-US/lines.json");
        var clip = project.Services.GetRequiredService<GroupEntryAuthoringService>().Copy(item.Group, [item.Entry.StableId]);
        var projects = project.Services.GetRequiredService<IProjectService>(); await projects.CloseAsync();
        var target = await projects.CreateAsync(Path.Combine(_root, "OtherLocalization"), "Other", new DefaultProjectOptions { TargetLocale = new("en-US"), AvailableLocales = [new("en-US")] }.ToConfiguration(), Contracts());
        var group = target.Services.GetRequiredService<IEditorDocumentService>().Groups.First().Key;
        var copy = (await target.Services.GetRequiredService<GroupEntryAuthoringService>().PasteAsync(group, clip, null)).Single(); var copiedKey = copy.Source.Parameters["content"].GetString()!;
        Assert.That(copiedKey, Is.Not.EqualTo(key)); var store = target.Services.GetRequiredService<LocalizationDocumentStore>();
        Assert.That(store.CaptureText(copiedKey).Text, Is.EqualTo("Cross-project")); Assert.That(store.CaptureText(copiedKey, "zh-CN").Text, Is.EqualTo("跨项目原文"));
        await target.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
    });

    [Test]
    public async Task GameTextMountedDefaultLanguageDraftLocaleSwitchAndSharedValue()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Text).Single();
                using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
                var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1100, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "GameTextInput"); input.Focus(); input.Text = "开发原文";
                    project.Services.GetRequiredService<IEditorLocalizationService>().ApplyLocale("en-US"); Assert.That(input.Text, Is.EqualTo("开发原文"));
                    input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control }); var texts = project.Services.GetRequiredService<GameTextAuthoringService>();
                    await PumpCharacterUi(() => entry.Source.Parameters.TryGetValue("content", out var value) && texts.Read(value.GetString()!) == "开发原文");
                    var key = entry.Source.Parameters["content"].GetString()!; Assert.That(key, Is.Not.EqualTo("开发原文"));
                    var second = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Text).Single(); second.Source.Parameters["content"] = JsonSerializer.SerializeToElement(key);
                    project.Services.GetRequiredService<IProjectContentSession>().Refresh(); Dispatcher.UIThread.RunJobs();
                    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Name == "GameTextHint" && block.IsVisible && block.Text!.Contains('2')), Is.True);
                    await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task GameTextBranchMountedFocusCommitRejectsReboundDraftAndRemovedOption()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); await WithLocalizationProject(async project =>
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var branch = await workspace.AddNodeAsync(GraphNodeKind.ChoiceBranch, 300, 0); workspace.SelectNode(branch);
                var model = project.Services.GetRequiredService<GalNet.Editor.Inspector.ViewModels.NodeInspectorControlViewModel>();
                var view = new GalNet.Editor.Inspector.Views.NodeInspectorControl { DataContext = model }; var outside = new Button { Content = "outside" };
                var window = new Window { Content = new StackPanel { Children = { view, outside } }, Width = 1100, Height = 800 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var texts = project.Services.GetRequiredService<GameTextAuthoringService>(); var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "GameTextInput");
                    input.Focus(); input.Text = "选项原文"; outside.Focus(); await PumpCharacterUi(() => texts.Read(branch.Source.Options![0].Text) == "选项原文");
                    var original = branch.Source.Options![0].Text; Assert.That(original, Is.Not.EqualTo("选项原文"));
                    input.Focus(); input.Text = "旧草稿";
                    WriteTranslation(project, """{"alternate":"其他原文"}"""); branch.Source.Options[0].Text = "alternate";
                    var content = project.Services.GetRequiredService<IProjectContentSession>(); content.Refresh(); Dispatcher.UIThread.RunJobs();
                    PressGroupKey(input, Key.Enter); Dispatcher.UIThread.RunJobs(); Assert.That(branch.Source.Options[0].Text, Is.EqualTo("alternate")); Assert.That(input.Text, Is.EqualTo("旧草稿"));
                    Assert.That(view.GetVisualDescendants().OfType<FieldEditorRow>().Any(row => row.Context?.Issue?.Code == "text.changed"), Is.True);
                    PressGroupKey(input, Key.Escape); Assert.That(input.Text, Is.EqualTo("其他原文"));
                    var binding = branch.Options[0].TextBinding!; var stale = texts.Register(texts.Capture("alternate", branch.Options[0].Id), "已删除");
                    try
                    {
                        project.Services.GetRequiredService<GraphAuthoringService>().RemoveBranchItem(branch.Id, branch.Options[0].Id);
                        Assert.ThrowsAsync<EditorOperationException>(async () => await binding.CommitAsync(new(JsonSerializer.SerializeToElement(stale.Token)), CancellationToken.None));
                        Assert.That(texts.Read("alternate"), Is.EqualTo("其他原文"));
                    }
                    finally { texts.Release(stale); }
                }
                finally { window.Close(); }
            }); return true;
        }, CancellationToken.None);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task GameTextNestedListCopyAndMoveRetainIndependentKeysAndEveryLanguage(bool hasDefault)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            var fields = new DynamicParameterTable([new(new FieldDescriptor("items", new(typeof(List<GameLineDto>))))]);
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules().Append(new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase("custom.lines", fields)])));
            await WithLocalizationProject(async project =>
            {
                if (hasDefault) WriteTranslation(project, """{"original":"原文"}"""); WriteTranslation(project, """{"original":"English"}""", "I18n/en-US/lines.json");
                var item = CharacterEntry(project); item.Entry.Type = "custom.lines"; item.Entry.Parameters = new() { ["items"] = JsonSerializer.SerializeToElement(new[] { new GameLineDto { Text = "original", Code = "original" } }) };
                using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, item.Group);
                var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1100, Height = 1000 }; window.Show(); Dispatcher.UIThread.RunJobs();
                try
                {
                    var list = view.GetVisualDescendants().OfType<StructuredFieldEditor>().Single(editor => editor.Context.Field.Id == "items"); list.IsExpanded = true; Dispatcher.UIThread.RunJobs();
                    var copy = list.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "copy"); Click(window, copy);
                    await PumpCharacterUi(() => item.Entry.Parameters["items"].GetArrayLength() == 2);
                    var key = item.Entry.Parameters["items"][1].GetProperty("Text").GetString()!;
                    Assert.That(key, Is.Not.EqualTo("original")); Assert.That(item.Entry.Parameters["items"][1].GetProperty("Code").GetString(), Is.EqualTo("original"));
                    var store = project.Services.GetRequiredService<LocalizationDocumentStore>(); Assert.That(store.CaptureText(key).Text, Is.EqualTo(hasDefault ? "原文" : null)); Assert.That(store.CaptureText(key, "en-US").Text, Is.EqualTo("English"));
                    Dispatcher.UIThread.RunJobs(); list = view.GetVisualDescendants().OfType<StructuredFieldEditor>().Single(editor => editor.Context.Field.Id == "items");
                    var up = list.GetVisualDescendants().OfType<Button>().Last(button => button.Name == "up"); Click(window, up);
                    await PumpCharacterUi(() => item.Entry.Parameters["items"][0].GetProperty("Text").GetString() == key);
                    Assert.That(store.CaptureText(key, "en-US").Text, Is.EqualTo("English"));
                    var history = project.Services.GetRequiredService<EditorHistories>().Content; await history.UndoAsync(); await history.UndoAsync();
                    Assert.That(item.Entry.Parameters["items"].GetArrayLength(), Is.EqualTo(1)); Assert.That(store.CaptureText(key).Text, Is.Null); Assert.That(store.CaptureText(key, "en-US").Text, Is.Null);
                }
                finally { window.Close(); }
            }, catalog: catalog); return true;
        }, CancellationToken.None);
    }
    public sealed class GameLineDto : IFieldDto
    {
        [Field(0), LocalizedText] public string Text { get; set; } = "";
        [Field(1)] public string Code { get; set; } = "";
        public static IReadOnlyList<FieldDescriptor> Schema { get; } = [new("text", new(typeof(string)), semantic: FieldValueSemantic.LocalizedText), new("code", new(typeof(string)))];
        [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<FieldDescriptor> Fields => Schema;
        public object? ReadField(string id) => id == "text" ? Text : Code;
        public void WriteField(string id, object? value) { if (id == "text") Text = (string)value!; else Code = (string)value!; }
    }
}
