using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task ScopedDtoOverrideCommitsMultipleEntryFieldsAsOneUndoAndSave()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            foreach (var key in new[] { "AddIcon", "DeleteRegularIcon", "ChevronUpRegular", "ChevronDownRegular" }) Application.Current.Resources[key] = StreamGeometry.Parse("M0,0 L8,8");
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "DtoPanels"), "DtoPanels", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1).Single(); entry.Type = BuiltinEntryIds.MoveLayer;
            var registry = scope.GetRequiredService<EditorRegistry>(); DtoEditorContext? editor = null;
            registry.RegisterDto<LayerMoveOptions>(context => { editor = context; return new(context.CreateDefaultForm()); }); registry.Freeze();
            using var panel = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            Assert.That(panel.FieldEditors, Is.SameAs(registry));
            var view = new GroupEditorPanelView { DataContext = panel }; var window = new Window { Content = view, Width = 1100, Height = 700 }; window.Show(); window.UpdateLayout();
            try
            {
                Assert.That(editor, Is.Not.Null); Assert.That(view.GetVisualDescendants().OfType<DtoEditorHost>().Any(host => host.Item?.Id == entry.StableId), Is.True);
                var original = entry.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                var patch = new Dictionary<string, FieldValue> { ["z"] = new(JsonSerializer.SerializeToElement(42)), ["duration"] = new(JsonSerializer.SerializeToElement(2)) };
                Assert.That(await editor!.CommitAsync(patch), Is.Null);
                Assert.That(entry.Parameters["z"], Is.EqualTo("42")); Assert.That(entry.Parameters["duration"], Is.EqualTo("2"));
                await history.UndoAsync(); Assert.That(entry.Parameters, Is.EquivalentTo(original)); Assert.That(history.CanUndo, Is.False);
                await history.RedoAsync(); await scope.GetRequiredService<IProjectContentSession>().SaveAsync();
                var json = File.ReadAllText(Path.Combine(project.RootPath, "Graph", group.Source.File!));
                Assert.That(json, Does.Contain("42")); Assert.That(scope.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
            }
            finally { window.Close(); panel.Dispose(); await projects.CloseAsync(); }
            Assert.That((await editor!.CommitAsync(new Dictionary<string, FieldValue> { ["z"] = new(JsonSerializer.SerializeToElement(6)) }))?.Code, Is.EqualTo("dto.obsolete"));
            return true;
        }, CancellationToken.None);
    }
}
