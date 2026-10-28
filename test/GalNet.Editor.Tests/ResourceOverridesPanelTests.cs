using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Assets;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task ResourcePatchMountedMatrixSelectsLocatesUndoesSavesReopensAndExports()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "Patches"), "Patches", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            await using var projectLifetime = project;
            var scope = project.Services; var files = scope.GetRequiredService<ProjectFileAuthoringService>();
            static byte[] Png(int size)
            {
                using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(size, size), new Avalonia.Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
                using var memory = new MemoryStream(); bitmap.Save(memory); return memory.ToArray();
            }
            var replacementBytes = Png(3);
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/original.png", Content: Png(2)));
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/replacement.png", Content: replacementBytes));
            var original = scope.GetRequiredService<IAssetCatalogService>().GetEntry("original.png")!.Id!;
            var replacement = scope.GetRequiredService<IAssetCatalogService>().GetEntry("replacement.png")!.Id!;
            var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, "layer.show").Single();
            var parameters = new Dictionary<string, JsonElement>(entry.Source.Parameters) { ["assetId"] = JsonSerializer.SerializeToElement(original) };
            scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, entry.Source, entry.Type, parameters);
            scope.GetRequiredService<GraphAuthoringService>().SetStoryPackage(group.Id, "bonus");
            var dock = scope.GetRequiredService<GalNet.Editor.Dock.EditorDockFactory>();
            var layout = dock.CreateLayout(); dock.InitLayout(layout);
            var model = scope.GetRequiredService<ResourceOverridesPanelViewModel>(); await model.RefreshAsync();
            var view = new ResourceOverridesPanelView { DataContext = model }; var window = new Window { Content = view, Width = 1300, Height = 700 };
            window.Show(); TimelinePump(window);
            try
            {
                Assert.That(model.Rows.Any(row => row.Id == original), Is.True, model.Message);
                TimelineClick(window, view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CreateResourcePatch"));
                await WaitForResourceUi(window, () => model.Columns.Count == 1);
                var id = model.Columns[0].Id;
                var name = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PatchName_" + id);
                name.Focus(); name.Text = "Clothes B";
                name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await WaitForResourceUi(window, () => model.Service.Read().Packages.Single().Name == "Clothes B");
                var picker = view.GetVisualDescendants().OfType<AssetPicker>().Single(control => control.Name == "Replacement_" + id + "_" + original);
                TimelineClick(window, picker.FindControl<Button>("PickerButton")!);
                await WaitForResourceUi(window, () => picker.ResourcePanel.Results.Any(item => item.Id == replacement) || picker.ResourcePanel.Hint.Length > 0);
                Assert.That(picker.ResourcePanel.Results.Any(item => item.Id == replacement), Is.True, picker.ResourcePanel.Hint);
                picker.ResourcePanel.List.SelectedItem = picker.ResourcePanel.Results.Single(item => item.Id == replacement);
                picker.ResourcePanel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await WaitForResourceUi(window, () => model.Service.Read().Packages.Single().Mappings.GetValueOrDefault(original) == replacement);
                var history = scope.GetRequiredService<EditorHistories>().Content;
                await history.UndoAsync(); Assert.That(model.Service.Read().Packages.Single().Mappings.ContainsKey(original), Is.False);
                await history.RedoAsync(); await model.RefreshAsync(); TimelinePump(window);
                for (var index = 0; index < 4; index++) model.Service.Create("Extra " + index);
                await model.RefreshAsync(); TimelinePump(window);
                foreach (var column in model.Columns)
                {
                    var header = view.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PatchName_" + column.Id);
                    var left = header.TranslatePoint(default, view)!.Value.X;
                    foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(button => button.Name is { } name &&
                        (name == "ExportPatch_" + column.Id || name == "DeletePatch_" + column.Id || name == "DiscardPatch_" + column.Id)))
                        Assert.That(button.TranslatePoint(new Avalonia.Point(button.Bounds.Width, 0), view)!.Value.X, Is.LessThanOrEqualTo(left + 260), "Column actions must not overlap the next patch.");
                }
                var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "ResourcePatchHorizontalScroll");
                scroll.Offset = new Avalonia.Vector(300, 0); TimelinePump(window); Assert.That(scroll.Offset.X, Is.GreaterThan(0));
                scroll.Offset = default; TimelinePump(window);
                TimelineClick(window, view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Locate_" + original));
                Assert.That(scope.GetRequiredService<AssetPanelViewModel>().SelectedEntry?.Path, Is.EqualTo("Assets/original.png"));
                var output = Path.Combine(_root, "patch.pak"); ((TestDialogs)services.GetRequiredService<IFileDialogService>()).SavePath = output;
                TimelineClick(window, view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ExportPatch_" + id));
                await WaitForResourceUi(window, () => File.Exists(output) && !model.Busy);
                var types = scope.GetRequiredService<GalNet.Core.Assets.IResourceTypeCatalog>();
                using (var pak = Archive.Deserialize("assets", File.ReadAllBytes(output), types))
                {
                    Assert.That(pak.Descriptor.Mappings[original], Is.EqualTo(replacement));
                    Assert.That(pak.GetAsset(replacement)!.ReadAllBytes(), Is.EqualTo(replacementBytes));
                }
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                { Directory.CreateDirectory(artifacts); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(artifacts, "resource-patches.png")); }
                var root = project.RootPath; window.Close(); await projects.CloseAsync(); project = await projects.OpenAsync(root);
                Assert.That(project.Configuration.Read(ResourceOverrideOptions.Section).Packages.Single(package => package.Id == id).Name, Is.EqualTo("Clothes B"));
                Assert.That(project.Services.GetRequiredService<IEditorDocumentService>().CurrentDocument.Nodes.Single(node => node.Id == group.Id).StoryPackage, Is.EqualTo("bonus"));
                await projects.CloseAsync(); return true;
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task GroupStoryPackageMountedInspectorCommitsOnBlurAndUsesProjectHistory()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "PackageInspector"), "Inspector", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            await using var projectLifetime = project;
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup); workspace.SelectNode(group);
            var model = scope.GetRequiredService<GalNet.Editor.Inspector.ViewModels.NodeInspectorControlViewModel>();
            var view = new GalNet.Editor.Inspector.Views.NodeInspectorControl { DataContext = model };
            var window = new Window { Content = view, Width = 600, Height = 700 }; window.Show(); TimelinePump(window);
            try
            {
                var input = view.FindControl<AutoCompleteBox>("StoryPackageInput")!;
                input.GetVisualDescendants().OfType<TextBox>().First().Focus(); input.Text = "bonus-ui";
                view.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, model.OpenGroupEditorCommand)).Focus();
                await WaitForResourceUi(window, () => group.Source.StoryPackage == "bonus-ui");
                await scope.GetRequiredService<EditorHistories>().Content.UndoAsync(); Assert.That(group.StoryPackage, Is.Empty);
                await scope.GetRequiredService<EditorHistories>().Content.RedoAsync(); Assert.That(group.StoryPackage, Is.EqualTo("bonus-ui"));
                window.Close(); await projects.CloseAsync(); return true;
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ResourcePatchHeaderInvalidDraftBlocksSaveAndGroupPackageHistoryRestoresAbsence()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "PatchDraft"), "Draft", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            await using var projectLifetime = project;
            var scope = project.Services; var authoring = scope.GetRequiredService<GraphAuthoringService>();
            var group = scope.GetRequiredService<EditorWorkspaceViewModel>().Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            authoring.SetStoryPackage(group.Id, "bonus");
            var history = scope.GetRequiredService<EditorHistories>().Content;
            await history.UndoAsync(); Assert.That(group.Source.StoryPackage, Is.Null);
            await history.RedoAsync(); Assert.That(group.Source.StoryPackage, Is.EqualTo("bonus"));
            var model = scope.GetRequiredService<ResourceOverridesPanelViewModel>();
            model.Service.Create("Clothes"); await model.RefreshAsync();
            model.Columns.Single().Priority = "bad";
            Assert.ThrowsAsync<InvalidOperationException>(() => scope.GetRequiredService<IProjectContentSession>().SaveAsync());
            Assert.That(model.Columns.Single().Priority, Is.EqualTo("bad"));
            model.Columns.Single().Priority = "5"; await scope.GetRequiredService<IProjectContentSession>().SaveAsync();
            Assert.That(model.Service.Read().Packages.Single().Priority, Is.EqualTo(5));
            var original = model.Service.Read().Packages.Single().Id;
            model.Service.Delete(original); await history.UndoAsync(); Assert.That(model.Service.Read().Packages.Single().Id, Is.EqualTo(original));
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
