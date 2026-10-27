using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task AudioRepeatAndCompletionUseNumericAndChoiceInputsWithProjectUndo()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AudioCompletion"), "Audio", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayAudio).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var view = new EntryListEditorControl { DataContext = page };
            var window = new Window { Content = view, Width = 1000, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var count = NumericControlTestSupport.FieldInput(view, "loopCount");
                Assert.That(count.Text, Is.EqualTo("1"));
                count.Text = "0"; PressGroupKey(count, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["loopCount"].GetInt32(), Is.Zero);
                var completion = view.GetVisualDescendants().OfType<ComboBox>().Single(input => input.DataContext is FieldEditorContext { Field.Id: "completion" });
                completion.SelectedItem = completion.Items.Cast<FieldEditorOption>().Single(option => option.Value.GetString() == "requeueTail"); TimelinePump(window);
                Assert.That(entry.Source.Parameters["completion"].GetString(), Is.EqualTo("requeueTail"));
                await history.UndoAsync(); Assert.That(entry.Source.Parameters["completion"].GetString(), Is.EqualTo("discard"));
                await history.UndoAsync(); Assert.That(entry.Source.Parameters["loopCount"].GetInt32(), Is.EqualTo(1));
                await history.RedoAsync(); await history.RedoAsync();
                Assert.That(entry.Source.Parameters["completion"].GetString(), Is.EqualTo("requeueTail"));
            }
            finally { window.Close(); page.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
