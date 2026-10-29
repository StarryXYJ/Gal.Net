using Avalonia.Headless;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task UiuxInspectorFollowsFocusBackToAlreadyActiveGroupAndRespectsLock()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxFocus"), "Focus", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Text).Single();
            var factory = project.Services.GetRequiredService<EditorDockFactory>(); var root = factory.CreateLayout(); factory.InitLayout(root);
            factory.OpenGroupEditor(group);
            var dockables = TimelineDockables(root).ToArray();
            var groupDocument = dockables.Single(item => item.Context is GroupEditorPanelViewModel);
            var page = (GroupEditorPanelViewModel)groupDocument.Context!; page.SelectedEntry = entry;
            var assetsDocument = dockables.Single(item => item.Context is AssetPanelViewModel);
            var host = dockables.Select(item => item.Context).OfType<InspectorHostViewModel>().Single();
            factory.SetFocusedDockable(root, assetsDocument);
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.Assets));
            factory.SetFocusedDockable(root, groupDocument);
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.GroupEditor));
            Assert.That(((EntryInspectorViewModel)host.CurrentInspectorViewModel!).Entry, Is.SameAs(entry));
            workspace.SelectedAsset = new GalNet.Editor.Models.AssetEntry { RelativePath = "Assets", FullPath = Path.Combine(project.RootPath, "Assets"), Name = "Assets", IsDirectory = true };
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.GroupEditor), "Background asset selection refresh must not steal the inspector.");
            factory.SetFocusedDockable(root, assetsDocument); host.IsLocked = true;
            factory.SetFocusedDockable(root, groupDocument);
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.Assets));
            host.IsLocked = false;
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.GroupEditor));
            factory.OpenPanel(EditorDockPanelIds.Log);
            var log = TimelineDockables(root).Single(item => item.Context is LogPanelViewModel);
            factory.SetFocusedDockable(root, log);
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.GroupEditor));
            host.IsLocked = true; factory.SetFocusedDockable(root, assetsDocument); factory.SetFocusedDockable(root, log); host.IsLocked = false;
            Assert.That(host.TargetPanelId, Is.EqualTo(EditorDockPanelIds.Assets), "Auxiliary focus must retain the last author target for unlocking.");
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
