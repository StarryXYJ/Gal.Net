using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private async Task CreateVariantImages(string root, ProjectFileAuthoringService files)
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "images")).FullName;
        using (var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            foreach (var name in new[] { "idle_coat", "idle_skirt", "smile_coat", "smile_skirt", "weap" }) bitmap.Save(Path.Combine(source, name + ".png"));
        await files.ExecuteAsync(new(ProjectFileAction.CreateDirectory, [], "Assets/alice"));
        await files.ExecuteAsync(new(ProjectFileAction.Import, Directory.GetFiles(source), "Assets/alice"));
    }

    [Test]
    public async Task MountedAtlasCreationConfigurationPreviewHistorySaveAllAndReopenUseProjectBuffers()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AtlasAuthor"), "Atlas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            await CreateVariantImages(project.RootPath, scope.GetRequiredService<ProjectFileAuthoringService>());
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var browser = scope.GetRequiredService<AssetPanelViewModel>(); browser.Navigate("Assets/alice"); await browser.NewVariantsCommand.ExecuteAsync(null);
            Assert.That(browser.ErrorText, Is.Empty); var path = Path.Combine(project.RootPath, "Assets/alice/variants.variants");
            Assert.That(File.Exists(path + ".meta"), Is.True); var id = workspace.SelectedAsset!.Id;
            await history.UndoAsync(); Assert.That(File.Exists(path), Is.False); Assert.That(File.Exists(path + ".meta"), Is.False); Assert.That(history.CanUndo, Is.False);
            await history.RedoAsync(); await workspace.SaveAsync(); history.Clear();
            workspace.SelectedAsset = scope.GetRequiredService<IAssetCatalogService>().GetEntry("alice/variants.variants");
            using var inspector = scope.GetRequiredService<AssetInspectorControlViewModel>(); var model = inspector.Variants!;
            Assert.That(model, Is.Not.Null); Assert.That(model.PreviewRows, Has.Count.EqualTo(2));
            var view = new AssetInspectorControl { DataContext = inspector }; var window = new Window { Content = view, Width = 800, Height = 1100 }; window.Show(); TimelinePump(window);
            try
            {
                var original = model.Buffer.Text;
                ComboBox Choice(string key) => view.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.DataContext is VariantPreviewRow row && row.Key == key);
                Choice("d1").SelectedItem = "smile"; Choice("d2").SelectedItem = "skirt"; TimelinePump(window);
                Assert.That(model.Actual, Does.StartWith("smile_skirt"));
                Choice("d1").SelectedItem = "weap"; TimelinePump(window); Assert.That(model.Actual, Does.StartWith("weap")); Assert.That(model.Requested, Does.Contain("d2=skirt"));
                Choice("d1").SelectedItem = "smile"; TimelinePump(window); Assert.That(model.Actual, Does.StartWith("smile_skirt"));
                await model.PreviewLoading; TimelinePump(window); Assert.That(model.Preview, Is.Not.Null);
                Assert.That(model.Buffer.Text, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                { scope.GetRequiredService<IEditorLocalizationService>().ApplyLocale(locale); TimelinePump(window); Assert.That(model.Buffer.Text, Is.EqualTo(original)); Assert.That(model.Actual, Does.StartWith("smile_skirt")); }
                var stale = model.PreviewRows[0]; model.Refresh(); stale.Value = "idle"; Assert.That(model.Actual, Does.StartWith("smile_skirt"));
                var properties = view.GetVisualDescendants().OfType<ImageVariantsEditor>().Single();
                Assert.That(properties.GetVisualDescendants().OfType<ComboBox>().Count(), Is.EqualTo(1));
                Assert.That(properties.FindControl<TextBox>("VariantsDefault"), Is.Null);
                Assert.That(properties.FindControl<TextBox>("VariantsJson"), Is.Null);
                Assert.That(properties.FindControl<Button>("VariantsApply"), Is.Null);
                properties.FindControl<ComboBox>("VariantsDefaultChoices")!.SelectedItem = "smile_skirt";
                await model.ConfigurationSaving;
                Assert.That(model.Buffer.Error, Is.Empty); await history.UndoAsync(); Assert.That(history.CanUndo, Is.False);
                Assert.That(model.Default, Is.EqualTo("idle_coat")); await history.RedoAsync(); Assert.That(model.Default, Is.EqualTo("smile_skirt"));
                history.Clear(); properties.GetVisualDescendants().OfType<TextBox>().First(box => box.Name == "VariantsDimensionLabel").Text = "Expression"; TimelinePump(window); await model.ConfigurationSaving;
                Assert.That(history.CanUndo, Is.True);
                await history.UndoAsync(); Assert.That(model.Dimensions[0].Label, Is.Not.EqualTo("Expression"));
                await history.RedoAsync(); Assert.That(model.Dimensions[0].Label, Is.EqualTo("Expression"));
                Assert.That(model.Dimensions[0].Key, Is.EqualTo("d1"));
                // Each selection enters working content immediately; Save All persists it after close.
                var buffer = model.Buffer; var root = project.RootPath; window.Close(); inspector.Dispose();
                await workspace.SaveAsync();
                await projects.CloseAsync(); Assert.That(buffer.IsReadOnly, Is.True); buffer.Text = "{}"; await buffer.CommitAsync();
                project = await projects.OpenAsync(root);
                var saved = project.Services.GetRequiredService<ProjectTextFiles>().Open("Assets/alice/variants.variants");
                using var json = JsonDocument.Parse(saved.Text); Assert.That(json.RootElement.GetProperty("default").GetString(), Is.EqualTo("smile_skirt"));
                Assert.That(json.RootElement.GetProperty("dimensions")[0].GetProperty("key").GetString(), Is.EqualTo("d1"));
                Assert.That(json.RootElement.GetProperty("dimensions")[0].GetProperty("label").GetString(), Is.EqualTo("Expression"));
                Assert.That(project.Services.GetRequiredService<IAssetCatalogService>().GetEntry("alice/variants.variants")!.Id, Is.EqualTo(id));
                await projects.CloseAsync(); return true;
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task AtlasResourceRefreshInfersPositionsPreservesNamesAndNeverWritesConfiguration()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AtlasRefresh"), "Atlas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var files = scope.GetRequiredService<ProjectFileAuthoringService>();
            await CreateVariantImages(project.RootPath, files);
            var browser = scope.GetRequiredService<AssetPanelViewModel>(); browser.Navigate("Assets/alice"); await browser.NewVariantsCommand.ExecuteAsync(null);
            var buffer = scope.GetRequiredService<ProjectTextFiles>().Open("Assets/alice/variants.variants");
            var assets = scope.GetRequiredService<IAssetCatalogService>();
            using var model = new ImageVariantsInspector(project.RootPath, buffer, projects, scope.GetRequiredService<IResourceTypeCatalog>(), assets, scope.GetRequiredService<IEditorLocalizationService>());
            var window = new Window { Content = new ImageVariantsEditor { DataContext = model } }; window.Show(); TimelinePump(window);
            try
            {
                model.Dimensions[0].Label = "Expression"; await model.ConfigurationSaving;
                var original = buffer.Text;
                var longer = Path.Combine(_root, "smile_skirt_day.png"); File.Copy(Path.Combine(_root, "images", "smile_skirt.png"), longer);
                await files.ExecuteAsync(new(ProjectFileAction.Import, [longer], "Assets/alice"));
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                await assets.RefreshAsync(); TimelinePump(window);
                Assert.That(model.Dimensions.Select(row => row.Key), Is.EqualTo(new[] { "d1", "d2", "d3" }));
                Assert.That(model.Dimensions[0].Label, Is.EqualTo("Expression"));
                Assert.That(model.PreviewRows[2].Options, Is.EqualTo(new[] { "day" }));
                Assert.That(buffer.Text, Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                model.DefaultChoice = "missing"; Assert.That(model.Default, Is.EqualTo("idle_coat"));
                // A missing saved default remains visible and can be repaired using only the dropdown.
                buffer.Text = original.Replace("idle_coat", "missing");
                Assert.That(model.DefaultChoice, Is.Null); Assert.That(model.DefaultPlaceholder, Does.Contain("missing"));
                Assert.That(model.CanConfigure, Is.True);
                model.DefaultChoice = "smile_skirt_day"; await model.ConfigurationSaving;
                Assert.That(model.Buffer.Error, Is.Empty); Assert.That(model.Diagnostics, Is.Empty);
                Assert.That(model.Default, Is.EqualTo("smile_skirt_day"));
                Assert.That(model.Dimensions[0].Key, Is.EqualTo("d1"));
                var beforeRefresh = buffer.Text; history.Clear(); model.Refresh();
                Assert.That(buffer.Text, Is.EqualTo(beforeRefresh)); Assert.That(history.CanUndo, Is.False);
                var dialogs = (TestDialogs)scope.GetRequiredService<IFileDialogService>(); dialogs.Folder = _root;
                await model.BrowseFolderCommand.ExecuteAsync(null);
                Assert.That(model.Diagnostics, Does.Contain("Assets")); Assert.That(buffer.Text, Is.EqualTo(beforeRefresh));
                dialogs.Folder = Path.Combine(project.RootPath, "Assets", "alice");
                await model.BrowseFolderCommand.ExecuteAsync(null);
                Assert.That(model.Folder, Is.EqualTo(".")); Assert.That(model.Diagnostics, Is.Empty); Assert.That(history.CanUndo, Is.False);
                return true;
            }
            finally { window.Close(); await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task AtlasDraftValidationSurvivesPanelCloseAndDependentFieldsUseWorkingDefinitions()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AtlasFieldsWorking"), "Atlas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            await CreateVariantImages(project.RootPath, scope.GetRequiredService<ProjectFileAuthoringService>());
            var browser = scope.GetRequiredService<AssetPanelViewModel>(); browser.Navigate("Assets/alice"); await browser.NewVariantsCommand.ExecuteAsync(null);
            await workspace.SaveAsync(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var buffer = scope.GetRequiredService<ProjectTextFiles>().Open("Assets/alice/variants.variants");
            using (var model = new ImageVariantsInspector(project.RootPath, buffer, projects, scope.GetRequiredService<IResourceTypeCatalog>(), scope.GetRequiredService<IAssetCatalogService>(), scope.GetRequiredService<IEditorLocalizationService>()))
            {
                model.Default = "missing"; await model.ConfigurationSaving; Assert.That(history.CanUndo, Is.False); Assert.That(model.Diagnostics, Is.Not.Empty);
            }
            Assert.CatchAsync(async () => await workspace.SaveAsync()); Assert.That(buffer.Text, Does.Contain("missing"));
            buffer.CancelCommand.Execute(null);
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var show = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowVariants).Single();
            show.Source.Parameters["assetId"] = JsonSerializer.SerializeToElement(workspace.SelectedAsset!.Id);
            var select = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.SelectVariants).Single();
            select.Source.Parameters["handleId"] = show.Source.Parameters["handleId"].Clone(); select.Source.Parameters["dimension"] = JsonSerializer.SerializeToElement("d1");
            select.Source.Parameters["value"] = JsonSerializer.SerializeToElement("unknown-saved-value");
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = select;
            show.IsParametersExpanded = false; select.IsParametersExpanded = true;
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                var query = scope.GetRequiredService<ProjectSceneTargets>(); await query.PrepareAsync(); TimelinePump(window); history.Clear();
                var combos = view.GetVisualDescendants().OfType<ComboBox>().Where(combo => combo.Name == "VariantFieldChoice" && combo.IsEffectivelyVisible).ToArray();
                Assert.That(combos, Has.Length.EqualTo(2));
                var values = combos.Single(combo => combo.Items.OfType<string>().Contains("weap")); Assert.That(values.SelectedItem, Is.Null);
                Assert.That(select.Source.Parameters["value"].GetString(), Is.EqualTo("unknown-saved-value"));
                values.SelectedItem = "weap"; TimelinePump(window); Assert.That(select.Source.Parameters["value"].GetString(), Is.EqualTo("weap"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync();
                // A committed configuration remains unsaved on disk but is the editor query's source.
                using var model = new ImageVariantsInspector(project.RootPath, buffer, projects, scope.GetRequiredService<IResourceTypeCatalog>(), scope.GetRequiredService<IAssetCatalogService>(), scope.GetRequiredService<IEditorLocalizationService>());
                model.DefaultChoice = "smile_skirt"; await model.ConfigurationSaving; await query.PrepareAsync(); TimelinePump(window);
                Assert.That(query.Before(group.Id, select.StableId).Targets.Single().Variants!.Selection!.Request["d1"], Is.EqualTo("smile"));
                Assert.That(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path)), Does.Not.Contain("smile_skirt"));
                var staleValue = select.Source.Parameters["value"].GetString(); values.SelectedItem = "smile"; TimelinePump(window);
                Assert.That(select.Source.Parameters["value"].GetString(), Is.EqualTo(staleValue));
                Assert.That(select.Source.Parameters["dimension"].GetString(), Is.EqualTo("d1"));
                history.Clear(); select.IsParametersExpanded = false; show.IsParametersExpanded = true; page.SelectedEntry = show; TimelinePump(window);
                var initial = view.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Name == "VariantFieldChoice" && combo.IsEffectivelyVisible && combo.Items.OfType<string>().Contains("skirt"));
                initial.SelectedItem = "skirt"; TimelinePump(window);
                Assert.That(show.Source.Parameters["selection"].GetProperty("d2").GetString(), Is.EqualTo("skirt"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync();
                // Unknown JSON is preserved and cannot be rewritten by the typed projection.
                buffer.Text = """{"folder":".","default":"idle_coat","future":9007199254740993}""";
                Assert.That(model.CanConfigure, Is.False); model.Default = "weap"; Assert.That(buffer.Text, Does.Contain("9007199254740993"));
                buffer.Text = """{"folder":".","default":"idle_coat","dimensions":[null]}""";
                Assert.That(model.CanConfigure, Is.False); Assert.That(model.Diagnostics, Does.Contain("invalid-dimension-key"));
                buffer.CancelCommand.Execute(null); window.Close(); await projects.CloseAsync(); return true;
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
        }, CancellationToken.None);
    }
}
