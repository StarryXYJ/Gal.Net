using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task VariableIntentsOwnHistoryWithoutWorkspaceAndRuntimeEditsNeverDirtyAuthorContent()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "UnifiedVariableState"), "Variables", new(), Contracts());
        var scope = project.Services;
        var author = scope.GetRequiredService<VariableAuthoringService>();
        var content = scope.GetRequiredService<IProjectContentSession>();
        var history = scope.GetRequiredService<EditorHistories>().Content;
        var declarations = scope.GetRequiredService<IVariableDefinitionService>();
        var documents = scope.GetRequiredService<IEditorDocumentService>();
        var variable = author.Add(VariableScope.Save);
        var uid = variable.DefaultValue.Uid;
        author.Update(VariableScope.Save, uid, "score", VariableType.Int, "3", "Score");
        var entry = new EditorEntryData
        {
            StableId = Guid.NewGuid().ToString("N"),
            Type = BuiltinEntryIds.Wait,
            Condition = "[score] > 0",
            Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1f) }
        };
        documents.Groups.Values.Single().Entries.Add(entry);
        await projects.SaveAsync();
        Assert.That(await projects.CheckUnsavedChangesAsync(), Is.False);

        var runtime = scope.GetRequiredService<EditorVariableService>();
        var preview = new VariableListEditorViewModel(declarations.GetDefinitions(VariableScope.Save), true,
            name => runtime.GetSnapshot(VariableScope.Save).GetValueOrDefault(name), (name, value) =>
            {
                var next = new Variable { Name = name }; next.SetValue(value);
                runtime.NotifyVariableChanged(VariableScope.Save, name, next);
            });
        var undoBeforeRuntimeEdit = history.UndoDescription;
        preview.Items.Single().CurrentIntValue = 42;
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GetSnapshot(VariableScope.Save)["score"].AsInt(), Is.EqualTo(42));
            Assert.That(author.Get(VariableScope.Save).Single().DefaultValue.AsInt(), Is.EqualTo(3));
            Assert.That(content.IsDirty, Is.False);
            Assert.That(history.IsDirty, Is.False);
            Assert.That(history.UndoDescription, Is.EqualTo(undoBeforeRuntimeEdit));
        });

        author.Update(VariableScope.Save, uid, "points", VariableType.Int, "9", "Updated");
        Assert.That(entry.Condition, Is.EqualTo("[points] > 0"));
        Assert.That(await projects.CheckUnsavedChangesAsync(), Is.True);
        await history.UndoAsync();
        Assert.That(entry.Condition, Is.EqualTo("[score] > 0"));
        Assert.That(author.Get(VariableScope.Save).Single().Name, Is.EqualTo("score"));
        Assert.That(history.IsDirty, Is.False);
        Assert.That(await projects.CheckUnsavedChangesAsync(), Is.False);
        await history.RedoAsync();
        await projects.SaveAsync();
        Assert.That(history.IsDirty, Is.False);
        await history.UndoAsync();
        Assert.That(history.IsDirty, Is.True);
        await history.RedoAsync();
        Assert.That(history.IsDirty, Is.False);
        await projects.CloseAsync();
        var reopened = await projects.OpenAsync(project.RootPath);
        Assert.That(reopened.Services.GetRequiredService<IVariableDefinitionService>().GetDefinitions(VariableScope.Save).Single().Name, Is.EqualTo("points"));
        Assert.That(await projects.CheckUnsavedChangesAsync(), Is.False);
        await projects.CloseAsync();
    }
}
