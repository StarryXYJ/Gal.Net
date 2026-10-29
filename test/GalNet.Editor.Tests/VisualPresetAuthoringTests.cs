using Avalonia.Headless;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task VisualPresetCreationAndTypeChangePersistExplicitProjectBindingsAndUndo()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var path = Path.Combine(_root, "VisualPresets");
            var project = await projects.CreateAsync(path, "Visual presets", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            try
            {
                var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
                var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.SpeedLinesPreset).Single();
                var initial = project.Configuration.ReadDefaultOptions().DefaultVisualPrograms["speedLines"];
                Assert.That(entry.Source.Parameters["program"].GetString(), Is.EqualTo(initial));
                Assert.That(entry.Source.Parameters["playbackHandleId"].GetString(), Is.Not.Empty);
                var text = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Text).Single();
                var history = project.Services.GetRequiredService<EditorHistories>().Content;
                history.Clear();
                text.Type = BuiltinEntryIds.SpeedLinesPreset;
                Assert.That(text.Source.Parameters["program"].GetString(), Is.EqualTo(initial));
                history.Undo();
                Assert.That(text.Type, Is.EqualTo(BuiltinEntryIds.Text));
                history.Redo();
                Assert.That(text.Source.Parameters["program"].GetString(), Is.EqualTo(initial));
                var id = entry.StableId; var groupId = group.Id;
                await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync();
                await projects.CloseAsync();
                project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == id);
                Assert.That(reopened.Source.Parameters["program"].GetString(), Is.EqualTo(initial));
                Assert.That(File.Exists(Path.Combine(path, "Assets/Effects/Default/SpeedLines.sksl")), Is.True);
            }
            finally { if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
