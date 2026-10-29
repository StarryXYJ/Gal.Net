using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
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
    [TestCase(BuiltinEntryIds.CrossFadeTransition, "oldHandleId")]
    [TestCase(BuiltinEntryIds.BlindsTransition, "oldHandleId")]
    [TestCase(BuiltinEntryIds.SlideTransition, "fromLayerHandleId")]
    [TestCase(BuiltinEntryIds.BlackFadeTransition, "fromLayerHandleId")]
    [TestCase(BuiltinEntryIds.WhiteFadeTransition, "fromLayerHandleId")]
    [TestCase(BuiltinEntryIds.ColorFadeTransition, "fromLayerHandleId")]
    public async Task TransitionUsesRealScenePickerHidesInternalOutputsAndPublishesStableTerminalLocator(string type, string input)
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TransitionPicker"), "Transitions", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
            workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayParticleEmitter);
            var entry = workspace.InsertEntriesInto(group, 2, 1, type).Single();
            workspace.InsertEntriesInto(group, 3, 1, BuiltinEntryIds.ShowLayer);
            var end = workspace.InsertEntriesInto(group, 4, 1, BuiltinEntryIds.Wait).Single();
            var output = SceneAuthoringIdentities.CreationSlot(type)!; var handle = entry.Source.Parameters[output].GetString(); var playback = entry.Source.Parameters["playbackHandleId"].GetString();
            var cross = type is BuiltinEntryIds.CrossFadeTransition or BuiltinEntryIds.BlindsTransition;
            entry.Source.Parameters[cross ? "assetId" : "toAssetId"] = JsonSerializer.SerializeToElement("incoming-image");
            entry.Source.Parameters[input] = JsonSerializer.SerializeToElement("unresolved-before");
            if (type == BuiltinEntryIds.BlindsTransition) entry.Source.Parameters["maskProgram"] = JsonSerializer.SerializeToElement("mask-program");
            entry.Source.Parameters["future"] = JsonSerializer.Deserialize<JsonElement>("[9007199254740993,null]");
            foreach (var item in group.Entries) item.IsParametersExpanded = ReferenceEquals(item, entry);
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(Guid.TryParse(handle, out _), Is.True); Assert.That(Guid.TryParse(playback, out _), Is.True);
                Assert.That(((IFieldBinding)entry.ParameterFields.Single(field => field.Id == output)).IsVisible, Is.False);
                Assert.That(((IFieldBinding)entry.ParameterFields.Single(field => field.Id == "playbackHandleId")).IsVisible, Is.False);
                Assert.That(view.GetVisualDescendants().OfType<TextBox>().Where(control => control.IsEffectivelyVisible).Any(control => control.Text == handle || control.Text == playback), Is.False);
                Assert.That(view.GetVisualDescendants().OfType<SchemaForm>().Any(), Is.True);
                Assert.That(view.GetVisualDescendants().OfType<SchemaForm>().SelectMany(item => item.Diagnostics), Is.Empty);
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                var original = entry.Source.Parameters[input].Clone();
                var button = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SceneTargetField" && control.IsEffectivelyVisible);
                TimelineClick(window, button); var picker = (SceneTargetPicker)((Flyout)button.Flyout!).Content!;
                var catalog = scope.GetRequiredService<SceneTargetCatalog>(); var snapshot = catalog.Query(group.Id, entry.StableId, target => target.Kind == SceneTargetKind.Layer);
                var choice = snapshot.Choices.Single(); Assert.That(choice.Target.Locator.EntryId, Is.EqualTo(layer.StableId));
                Assert.That(picker.ResourcePanel.Results, Has.Count.EqualTo(2), "Only None and the earlier layer; no particle, current output or future layer.");
                Assert.That(history.CanUndo, Is.False);
                picker.ResourcePanel.Search.Text = choice.Item.Label; TimelinePump(window); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters[input].GetString(), Is.EqualTo(layer.Source.Parameters["handleId"].GetString()));
                var target = catalog.Query(group.Id, end.StableId).Choices.Single(item => item.Target.Handle == handle).Target;
                Assert.That(target.Locator, Is.EqualTo(new SceneLocator(group.Id, entry.StableId, output)));
                Assert.That(target.Presence, Is.EqualTo(TargetPresence.Definite)); Assert.That(target.Values["opacity"], Is.EqualTo(1));
                Assert.That(catalog.Resolve(snapshot, choice.Item.Id), Is.Null, "An old selection lease cannot survive author edits.");
                await history.UndoAsync(); Assert.That(JsonElement.DeepEquals(entry.Source.Parameters[input], original), Is.True); Assert.That(history.CanUndo, Is.False);
                await history.RedoAsync(); Assert.That(entry.Source.Parameters[output].GetString(), Is.EqualTo(handle)); Assert.That(entry.Source.Parameters["playbackHandleId"].GetString(), Is.EqualTo(playback));
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var entryId = entry.StableId; var endId = end.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == entryId);
                Assert.That(reopened.Source.Parameters["future"][0].GetInt64(), Is.EqualTo(9007199254740993L));
                Assert.That(reopened.Source.Parameters[output].GetString(), Is.EqualTo(handle));
                var restored = project.Services.GetRequiredService<SceneTargetCatalog>().Query(groupId, endId).Choices.Single(item => item.Target.Handle == handle).Target;
                Assert.That(restored.Locator, Is.EqualTo(target.Locator)); Assert.That(restored.Values["opacity"], Is.EqualTo(1));
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
