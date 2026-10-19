using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task GeneratedComplexFieldsUseProjectHistoryAndReopenWithoutLosingUnknownOrAbsentValues()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("structured", [DefaultPrimitiveEntryBase.ForDto<StructuredAuthorOptions>("custom.structure")])]);
            using var services = Services(catalog: catalog); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "StructuredForms"), "Forms", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, "custom.structure").Single();
            entry.Source.Parameters = new()
            {
                ["details"] = JsonSerializer.Deserialize<JsonElement>("{\"Title\":\"before\",\"opaque\":{\"keep\":true}}"),
                ["numbers"] = JsonSerializer.Deserialize<JsonElement>("[3,4]")
            };
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 1000 }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var root = view.GetVisualDescendants().OfType<SchemaForm>().Single(form => form.Schema?.Any(field => field.Id == "details") == true);
                var details = (StructuredFieldEditor)root.Editors.Single(item => item.Context.Field.Id == "details").Control;
                var numbers = (StructuredFieldEditor)root.Editors.Single(item => item.Context.Field.Id == "numbers").Control;
                Assert.That(details.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Any(), Is.True, "The themed Expander header must exist so authors can open the object.");
                Assert.That(history.CanUndo, Is.False); Assert.That(entry.Source.Parameters.ContainsKey("optional"), Is.False);
                details.IsExpanded = true; numbers.IsExpanded = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var title = details.GetVisualDescendants().OfType<SchemaForm>().Single().Contexts.Single(); title.Text = "after";
                Assert.That(await title.CommitTextAsync(), Is.True); Assert.That(entry.Source.Parameters["details"].GetProperty("opaque").GetProperty("keep").GetBoolean(), Is.True);
                var previous = numbers.GetVisualDescendants().OfType<SchemaForm>().First().Contexts.Single();
                numbers.GetVisualDescendants().OfType<Button>().First(button => button.Name == "down").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                Assert.That(entry.Source.Parameters["numbers"][0].GetInt32(), Is.EqualTo(4)); Assert.That(await previous.CommitValueAsync(99), Is.False);
                await history.UndoAsync(); Assert.That(entry.Source.Parameters["numbers"][0].GetInt32(), Is.EqualTo(3));
                await history.UndoAsync(); Assert.That(entry.Source.Parameters["details"].GetProperty("Title").GetString(), Is.EqualTo("before")); Assert.That(history.CanUndo, Is.False);
                await history.RedoAsync(); await history.RedoAsync();
                var optional = root.Contexts.Single(context => context.Field.Id == "optional"); Assert.That(await optional.SetNullAsync(), Is.True);
                await workspace.SaveAsync(); Assert.That(entry.Source.Parameters["optional"].ValueKind, Is.EqualTo(JsonValueKind.Null));
                await history.UndoAsync(); Assert.That(entry.Source.Parameters.ContainsKey("optional"), Is.False); Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.True);
                var localization = project.Services.GetRequiredService<IEditorLocalizationService>();
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                    Assert.That(details.AddText, Is.EqualTo(localization["Form.AddElement"]));
                    Assert.That(view.GetVisualDescendants().OfType<FieldEditorActions>().First().OmitText, Is.EqualTo(localization["Form.Clear"]));
                    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "title"), Is.True, "Custom field IDs must have readable labels without translation keys.");
                }
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var stableId = entry.StableId;
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p6b-structured-group.png")); }
                window.Close(); page.Dispose(); await projects.CloseAsync();
                project = await projects.OpenAsync(path); var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(item => item.StableId == stableId);
                Assert.That(reopened.Source.Parameters["details"].GetProperty("Title").GetString(), Is.EqualTo("after"));
                Assert.That(reopened.Source.Parameters["details"].GetProperty("opaque").GetProperty("keep").GetBoolean(), Is.True);
                Assert.That(reopened.Source.Parameters["numbers"][0].GetInt32(), Is.EqualTo(4)); Assert.That(reopened.Source.Parameters.ContainsKey("optional"), Is.False);
                await projects.CloseAsync();
            }
            finally { window.Close(); page.Dispose(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}

[GenerateFieldSchema]
public partial class StructuredAuthorOptions
{
    [Field(0)] public StructuredAuthorDetails? Details { get; set; }
    [Field(1)] public List<int>? Numbers { get; set; }
    [Field(2, AllowNull = true)] public int? Optional { get; set; }
}

[GenerateFieldSchema]
public partial class StructuredAuthorDetails
{
    [Field(0)] public string Title { get; set; } = "";
}
