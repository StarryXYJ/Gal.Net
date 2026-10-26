using GalNet.Contracts.Builtins.Assets;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using GalNet.GameHost.Content;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task CustomConfigurationMountedFormPreservesUnknownDataThroughHistorySaveExportAndLoading()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            Avalonia.Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "CustomConfiguration"), "Custom", new(), Contracts());
            project.Configuration.SetRaw("developer.options", new(1, JsonSerializer.SerializeToElement(new { count = 2, limit = 5, future = new[] { 42 } })));
            project.Configuration.SetRaw("opaque", new(7, JsonSerializer.SerializeToElement(new { kept = true })));
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var view = new ProjectSettingsPanelView { DataContext = panel };
            var window = new Window { Width = 800, Height = 900, Content = view }; window.Show(); window.UpdateLayout();
            try
            {
                var host = view.GetVisualDescendants().OfType<DtoEditorHost>().Single(host => host.Item?.Id == "developer.options");
                Assert.That(host.Diagnostics, Is.Empty);
                var form = host.GetVisualDescendants().OfType<SchemaForm>().Single();
                Assert.That(form.Diagnostics, Is.Empty);
                var input = host.GetVisualDescendants().OfType<TextBox>().Single(input => input.DataContext is FieldEditorContext context && context.Field.Id == "count");
                input.Text = "4";
                Assert.That(project.Configuration.Sections["developer.options"].Data.GetProperty("count").GetInt32(), Is.EqualTo(2));
                await panel.CommitDraftAsync();
                var old = Setting(panel, "count", "developer.options");
                Assert.That(project.Configuration.Sections["developer.options"].Data.GetProperty("count").GetInt32(), Is.EqualTo(4));
                await panel.UndoRedoHistory.UndoAsync();
                Assert.That(project.Configuration.Sections["developer.options"].Data.GetProperty("count").GetInt32(), Is.EqualTo(2));
                Assert.That(await old.CommitAsync(new(JsonSerializer.SerializeToElement(3)), CancellationToken.None), Is.Not.Null);
                await panel.UndoRedoHistory.RedoAsync();
                await projects.SaveAsync();
                var resources = BuiltinResourceTypes.CreateCatalog(); var gallery = BuiltinGalleryTypes.CreateCatalog(resources);
                var export = await GamePackageExporter.ExportAsync(project.Id, project.Name, project.RootPath, Path.Combine(_root, "Package"), resources, gallery,
                    project.Services.GetRequiredService<IEntryCatalog>(), project.Configurations);
                Assert.That(export.Success, Is.True, export.Error);
                var installed = await GameInstallation.OpenAsync(export.PackagePath!);
                var loaded = await installed.CreateContentProvider(resources, gallery, new GameContentLoadOptions { Configurations = project.Configurations }).LoadAsync();
                Assert.That(loaded.Configuration.Sections["developer.options"].Data.GetProperty("count").GetInt32(), Is.EqualTo(4));
                Assert.That(loaded.Configuration.Sections["developer.options"].Data.GetProperty("future")[0].GetInt32(), Is.EqualTo(42));
                Assert.That(loaded.Configuration.Sections["opaque"].Version, Is.EqualTo(7));
                panel.Dispose(); window.Close();
                await projects.CloseAsync(); await projects.OpenAsync(project.RootPath);
                Assert.That(projects.Current!.Configuration.Sections["developer.options"].Data.GetProperty("count").GetInt32(), Is.EqualTo(4));
                await projects.CloseAsync();
            }
            finally { panel.Dispose(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ConfigurationHistoryRestoresAbsentSectionsAndRejectedDraftsRemainEditable()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "ConfigurationDraft"), "Custom", new(), Contracts());
        var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
        await Stage(panel, "count", 4, "developer.options"); await Stage(panel, "limit", 3, "developer.options");
        Assert.ThrowsAsync<InvalidDataException>(panel.CommitDraftAsync);
        Assert.That(project.Configuration.Sections.ContainsKey("developer.options"), Is.False);
        await Stage(panel, "limit", 6, "developer.options"); await panel.CommitDraftAsync();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        await history.UndoAsync(); Assert.That(project.Configuration.Sections.ContainsKey("developer.options"), Is.False);
        await history.RedoAsync(); Assert.That(project.Configuration.Sections["developer.options"].Data.GetProperty("count").GetInt32(), Is.EqualTo(4));
        await Stage(panel, "caption", null, "developer.options"); await projects.SaveAsync();
        Assert.That(project.Configuration.Sections["developer.options"].Data.GetProperty("caption").ValueKind, Is.EqualTo(JsonValueKind.Null));
        await projects.CloseAsync();
    }
}
