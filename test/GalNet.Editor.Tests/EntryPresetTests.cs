using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Controls;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task BuiltinWeatherPresetsApplyToParticlesAndPreserveInstanceIdentity()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var path = Path.Combine(_root, "BuiltinWeatherPresets");
            var project = await projects.CreateAsync(path, "Builtin presets", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            try
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayParticleEmitter).Single();
                var originalId = entry.Source.Parameters["instanceId"].GetString();
                var presets = project.Services.GetRequiredService<EntryPresetService>();
                var builtins = presets.List(entry.Type);
                Assert.That(builtins, Has.Count.EqualTo(5));
                Assert.That(builtins.All(item => item.IsBuiltin), Is.True);
                await presets.ApplyAsync(group.Id, entry.Source, builtins[0].Id);
                Assert.That(entry.Source.Parameters["instanceId"].GetString(), Is.EqualTo(originalId));
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("particleTexture").GetString(), Is.EqualTo(BuiltinVisualResources.WeatherTextures["Rain"]));
                Assert.ThrowsAsync<InvalidOperationException>(async () => await presets.DeleteAsync(builtins[0].Id));
                await presets.RenameAsync(builtins[0].Id, "New rain");
                Assert.That(presets.List(entry.Type)[0].Name, Is.EqualTo("New rain"));
                Assert.That(File.Exists(Path.Combine(path, "Buildin/Presets/entries.json")), Is.True);
                File.Delete(Path.Combine(path, "Buildin/Presets/entries.json"));
                Assert.Throws<InvalidDataException>(() => presets.List(entry.Type));
            }
            finally { if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryPresetHistoryRefusesExternalChanges()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var path = Path.Combine(_root, "PresetExternalConflict");
            var project = await projects.CreateAsync(path, "Preset conflict", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            try
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
                await project.Services.GetRequiredService<EntryPresetService>().AddAsync(group.Id, entry.Source);
                var pathName = Path.Combine(path, "Presets/entries.json");
                var external = File.ReadAllText(pathName) + " ";
                File.WriteAllText(pathName, external);
                var history = project.Services.GetRequiredService<EditorHistories>().Content;
                Assert.ThrowsAsync<IOException>(async () => await history.UndoAsync());
                Assert.That(File.ReadAllText(pathName), Is.EqualTo(external));
                Assert.That(history.CanUndo, Is.True);
            }
            finally { if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryPresetPopupCreatesSavedItemAndFocusesItsName()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var path = Path.Combine(_root, "PresetPopup");
            var project = await projects.CreateAsync(path, "Preset popup", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.SpeedLinesPreset).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            Assert.That(page.EntryPresets, Is.Not.Null);
            page.SelectedEntry = entry;
            var listView = new EntryListEditorControl { DataContext = page };
            var inspectorModel = new EntryInspectorViewModel(page);
            var inspector = new EntryInspectorView { DataContext = inspectorModel };
            var container = new StackPanel(); container.Children.Add(listView); container.Children.Add(inspector);
            var bindingWindow = new Window { Content = container, Width = 1000, Height = 700 };
            bindingWindow.Show(); Dispatcher.UIThread.RunJobs(); bindingWindow.UpdateLayout();
            var groupButton = listView.GetVisualDescendants().OfType<EntryPresetButton>().Single();
            var inspectorButton = inspector.GetVisualDescendants().OfType<EntryPresetButton>().Single();
            Assert.That(groupButton.Page, Is.SameAs(page)); Assert.That(groupButton.Entry, Is.SameAs(entry));
            Assert.That(inspectorButton.Page, Is.SameAs(page)); Assert.That(inspectorButton.Entry, Is.SameAs(entry));
            bindingWindow.Close(); inspectorModel.Dispose();
            var button = new EntryPresetButton { Page = page, Entry = entry, Content = "Presets" };
            var window = new Window { Content = button, Width = 500, Height = 400 };
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                Click(window, button); Dispatcher.UIThread.RunJobs();
                Assert.That(FlyoutBase.GetAttachedFlyout(button), Is.Not.Null, "Preset button should open a flyout on click.");
                var popup = (StackPanel)((Flyout)FlyoutBase.GetAttachedFlyout(button)!).Content!;
                var add = popup.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "AddEntryPreset");
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var i = 0; i < 100 && !File.Exists(Path.Combine(path, "Presets/entries.json")); i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
                for (var i = 0; i < 100 && !popup.GetVisualDescendants().OfType<TextBox>().Any(item => item.Name == "EntryPresetName"); i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
                Dispatcher.UIThread.RunJobs();
                var name = popup.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "EntryPresetName");
                Assert.That(File.Exists(Path.Combine(path, "Presets/entries.json")), Is.True);
                Assert.That(name.IsFocused, Is.True);
                Assert.That(name.SelectionEnd - name.SelectionStart, Is.EqualTo(name.Text!.Length));
                Assert.That(name.ContextMenu!.Items.OfType<MenuItem>().Count(), Is.EqualTo(1));
                Assert.That(popup.GetVisualDescendants().OfType<Button>().Any(item => item.Name == "ApplyEntryPreset"), Is.True);
                var originalName = name.Text;
                name.Text = "Discarded"; PressGroupKey(name, Key.Escape);
                Assert.That(name.Text, Is.EqualTo(originalName));
                Assert.That(page.EntryPresets!.List(entry.Type).Single().Name, Is.EqualTo(originalName));
                name.Text = "Renamed"; PressGroupKey(name, Key.Enter);
                var service = page.EntryPresets!;
                for (var i = 0; i < 100 && service.List(entry.Type).Single().Name != "Renamed"; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
                Assert.That(service.List(entry.Type).Single().Name, Is.EqualTo("Renamed"));
                var renamed = popup.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "EntryPresetName");
                renamed.ContextMenu!.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for (var i = 0; i < 100 && service.List(entry.Type).Count > 0; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
                Assert.That(service.List(entry.Type), Is.Empty);
                await project.Services.GetRequiredService<EditorHistories>().Content.UndoAsync();
                Assert.That(service.List(entry.Type).Single().Name, Is.EqualTo("Renamed"));
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EntryPresetPublishesIndependentlyAndSupportsApplyAndHistory()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var path = Path.Combine(_root, "EntryPresets");
            var project = await projects.CreateAsync(path, "Entry presets", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            try
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                var source = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.SpeedLinesPreset).Single();
                var target = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.SpeedLinesPreset).Single();
                var fields = project.Services.GetRequiredService<EntryFieldAuthoringService>();
                var values = source.Source.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
                values["speed"] = JsonSerializer.SerializeToElement(7f);
                fields.SetParameters(group.Id, source.Source, source.Type, values);
                var oldTargetId = target.Source.Parameters["playbackHandleId"].GetString();
                var presets = project.Services.GetRequiredService<EntryPresetService>();
                var preset = await presets.AddAsync(group.Id, source.Source);
                var file = Path.Combine(path, "Presets/entries.json");
                Assert.That(File.Exists(file), Is.True);
                Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(path, "Graph", group.Source.File!)), Does.Not.Contain(source.StableId));
                Assert.That(preset.Parameters.ContainsKey("playbackHandleId"), Is.False);
                var compiled = Path.Combine(path, "Output", "preset-check");
                await new ProjectContentBuilder(project.Services.GetRequiredService<IEntryCatalog>()).BuildAsync(path, compiled);
                Assert.That(Directory.Exists(Path.Combine(compiled, "Presets")), Is.False);
                var buffer = project.Services.GetRequiredService<ProjectTextFiles>().Open("Presets/entries.json");
                await presets.RenameAsync(preset.Id, "My preset");
                Assert.That(buffer.Text, Does.Contain("My preset"));
                await presets.ApplyAsync(group.Id, target.Source, preset.Id);
                Assert.That(target.Source.Parameters["speed"].GetSingle(), Is.EqualTo(7));
                Assert.That(target.Source.Parameters["playbackHandleId"].GetString(), Is.EqualTo(oldTargetId));
                var history = project.Services.GetRequiredService<EditorHistories>().Content;
                await history.UndoAsync();
                Assert.That(target.Source.Parameters["speed"].GetSingle(), Is.Not.EqualTo(7));
                await presets.DeleteAsync(preset.Id);
                Assert.That(presets.List(source.Type), Is.Empty);
                await history.UndoAsync();
                Assert.That(presets.List(source.Type).Single().Name, Is.EqualTo("My preset"));
                buffer.Text += " ";
                Assert.ThrowsAsync<InvalidOperationException>(async () => await presets.RenameAsync(preset.Id, "Conflict"));
                Assert.That(presets.List(source.Type).Single().Name, Is.EqualTo("My preset"));
            }
            finally { if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
