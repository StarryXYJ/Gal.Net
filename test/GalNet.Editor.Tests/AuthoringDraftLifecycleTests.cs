using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
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
    public async Task AuthoringDraftsReopenInMountedGroupAndKeepOpaqueValuesThroughEditsCopyUndoAndSave()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "DraftLifecycle"), "Drafts", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var text = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Text).Single(); text.Parameters = new() { ["content"] = "original" };
            text.Source.Parameters["speaker"] = JsonSerializer.SerializeToElement(new { invalidStringValue = true });
            text.Source.Parameters["future"] = JsonSerializer.Deserialize<JsonElement>("[null,9007199254740993,{\"kept\":true}]");
            var particle = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayParticleEmitter).Single(); particle.Parameters = [];
            var documents = project.Services.GetRequiredService<IEditorDocumentService>();
            documents.Groups[group.Id].Entries.Add(new() { StableId = "opaque-line", Type = "custom.future", Condition = "(", Parameters = new() { ["payload"] = JsonSerializer.Deserialize<JsonElement>("{\"nested\":[null,1.234567890123456789],\"enabled\":true}") } });
            documents.SynchronizeGroups();
            await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var textId = text.StableId; var partialId = particle.StableId;
            await projects.CloseAsync(); project = await projects.OpenAsync(path); workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); group = workspace.Nodes.Single(node => node.Id == groupId);
            text = group.Entries.Single(entry => entry.StableId == textId); particle = group.Entries.Single(entry => entry.StableId == partialId);
            var opaque = group.Entries.Single(entry => entry.StableId == "opaque-line");
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, groupId);
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 1100 }; window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.That(text.HasUnknownParameters, Is.True); Assert.That(opaque.IsUnknownType, Is.True); Assert.That(particle.Source.Parameters, Is.Empty);
                var localization = project.Services.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); Dispatcher.UIThread.RunJobs();
                    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsVisible && block.Text == localization["Entry.UnknownParametersPreserved"]), Is.True);
                }
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var input = view.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "GameTextInput");
                input.Text = "edited"; Dispatcher.UIThread.RunJobs();
                input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control });
                await PumpCharacterUi(() => project.Services.GetRequiredService<GameTextAuthoringService>().Read("original") == "edited");
                Assert.That(text.Source.Parameters["speaker"].ValueKind, Is.EqualTo(JsonValueKind.Object), "Editing one dedicated text field must not reparse and coerce another invalid string field.");
                await history.UndoAsync(); Assert.That(text.Parameters["content"], Is.EqualTo("original")); Assert.That(history.CanUndo, Is.False); await history.RedoAsync();
                var authoring = project.Services.GetRequiredService<GroupEntryAuthoringService>(); var payload = opaque.Source.Parameters["payload"].Clone();
                var copiedId = (await authoring.PasteAsync(groupId, authoring.Copy(groupId, [opaque.StableId]), null)).Single();
                var copied = group.Entries.Single(entry => entry.StableId == copiedId);
                Assert.That(copied.StableId, Is.Not.EqualTo(opaque.StableId)); Assert.That(copied.Type, Is.EqualTo(opaque.Type));
                Assert.That(JsonElement.DeepEquals(copied.Source.Parameters["payload"], payload), Is.True);
                await history.UndoAsync(); Assert.That(group.Entries.Contains(copied), Is.False); await history.RedoAsync();
                workspace.SelectNode(group); var graphClip = workspace.CopySelection(); await workspace.PasteAsync(graphClip, 800, 300);
                var pastedGroup = workspace.SelectedNodes.Single();
                Assert.That(pastedGroup.Entries.Count(entry => entry.Type == "custom.future"), Is.EqualTo(2));
                await history.UndoAsync(); Assert.That(workspace.Nodes.Contains(pastedGroup), Is.False); await history.RedoAsync();
                page.ChangeType(opaque, BuiltinEntryIds.Wait); await workspace.SaveAsync(); await history.UndoAsync();
                Assert.That(opaque.Type, Is.EqualTo("custom.future")); Assert.That(JsonElement.DeepEquals(opaque.Source.Parameters["payload"], payload), Is.True);
                Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.True, "Undo after saving must restore the earlier author value as a new unsaved draft.");
                await workspace.SaveAsync(); Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
                var validation = new ProjectValidationService(path).Validate(catalog: project.Services.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>());
                Assert.That(validation.Success, Is.False); Assert.That(validation.Diagnostics.Any(item => item.Code == "entry.typeUnknown"), Is.True);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "authoring-drafts.png")); }
                window.Close(); page.Dispose(); await projects.CloseAsync();
                project = await projects.OpenAsync(path); var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries;
                Assert.That(reopened.Single(entry => entry.StableId == textId).Parameters["content"], Is.EqualTo("original"));
                Assert.That(project.Services.GetRequiredService<GameTextAuthoringService>().Read("original"), Is.EqualTo("edited"));
                Assert.That(reopened.Single(entry => entry.StableId == textId).Source.Parameters["speaker"].ValueKind, Is.EqualTo(JsonValueKind.Object));
                Assert.That(reopened.Count(entry => entry.Type == "custom.future"), Is.EqualTo(2));
                Assert.That(reopened.Single(entry => entry.StableId == partialId).Source.Parameters, Is.Empty);
                await projects.CloseAsync();
            }
            finally { window.Close(); page.Dispose(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
