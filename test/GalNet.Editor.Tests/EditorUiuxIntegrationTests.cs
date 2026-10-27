using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Dock.Model.Controls;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Themes;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task UiuxVariableEditorsMountCompletionControlsAndSaveOtherValidDrafts()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxCompletion"), "Completion", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.SetVariable).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var invalid = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.Wait).Single();
            var edits = project.Services.GetRequiredService<EntryEditSession>();
            var invalidState = edits.Get(invalid, invalid.DetailForm!.Fields.Single());
            invalidState.Update("invalid", true, null, false);
            var host = new DtoEditorHost { Item = entry.DetailForm!.Item, Registry = page.FieldEditors };
            var window = new Window { Content = host, Width = 900, Height = 700 };
            window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") });
            window.Show(); TimelinePump(window);
            Assert.That(host.GetVisualDescendants().OfType<ComboBox>().Any(control => control.Name == "VariableNameInput"), Is.True,
                string.Join("; ", host.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Diagnostics).Select(issue => issue.Message)));
            var expression = host.GetVisualDescendants().OfType<GalNet.Editor.Controls.ConditionEditorControl>().Single();
            expression.Text = "1 + 2";
            Assert.ThrowsAsync<InvalidDataException>(async () => await workspace.SaveAsync());
            Assert.That(entry.Source.Parameters["expression"].GetString(), Is.EqualTo("1 + 2"));
            Assert.That(edits.PendingCount, Is.EqualTo(1));
            invalidState.Clear(); window.Close(); await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxNestedDraftCreatesAnOmittedObjectAndRejectsLaterSourceChanges()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxNested"), "Nested", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.MoveLayer).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            entry.Source.Parameters.Remove("transform");
            var edits = project.Services.GetRequiredService<EntryEditSession>();
            var root = edits.Get(entry, entry.DetailForm!.Fields.Single(field => field.Id == "transform"));
            var child = root.Child!(new GalNet.Core.Parameters.FieldDescriptor("X", new(typeof(float))), "X");
            child.Update("2.5", true, null, false); await workspace.SaveAsync();
            Assert.That(entry.Source.Parameters["transform"].GetProperty("X").GetSingle(), Is.EqualTo(2.5));
            Assert.That(edits.PendingCount, Is.Zero);
            child.Update("4.5", true, null, false);
            entry.Source.Parameters["transform"] = JsonSerializer.SerializeToElement(new { X = 3f });
            project.Services.GetRequiredService<IProjectContentSession>().Refresh();
            entry.Source.Parameters["transform"] = JsonSerializer.SerializeToElement(new { X = 2.5f });
            project.Services.GetRequiredService<IProjectContentSession>().Refresh();
            Assert.That(child.IsConflicted, Is.True, "Restoring equal source JSON cannot revive a draft whose source identity already changed.");
            Assert.ThrowsAsync<InvalidDataException>(async () => await workspace.SaveAsync());
            Assert.That(entry.Source.Parameters["transform"].GetProperty("X").GetSingle(), Is.EqualTo(2.5));
            child.Clear(); child.Update("5.5", true, null, false);
            var sourceType = entry.Source.Type;
            entry.Source.Type = BuiltinEntryIds.Wait;
            project.Services.GetRequiredService<IProjectContentSession>().Refresh();
            entry.Source.Type = sourceType;
            project.Services.GetRequiredService<IProjectContentSession>().Refresh();
            Assert.That(child.IsConflicted, Is.True, "A restored type must not revive input captured before the type change.");
            Assert.ThrowsAsync<InvalidDataException>(async () => await workspace.SaveAsync());
            child.Clear(); await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxProjectOwnedPanelsRemainLiveAfterClosingAndReopeningTheirDocks()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxReopen"), "Reopen", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var factory = project.Services.GetRequiredService<EditorDockFactory>(); var root = factory.CreateLayout(); factory.InitLayout(root);
            var asset = project.Services.GetRequiredService<AssetPanelViewModel>();
            factory.ToggleGlobalPanel(EditorDockPanelIds.Assets); factory.OpenPanel(EditorDockPanelIds.Assets);
            File.WriteAllText(Path.Combine(project.RootPath, "reopen-fixture.txt"), "fixture"); asset.SearchText = "reopen-fixture"; asset.Refresh();
            Assert.That(asset.Entries.Any(item => item.Name == "reopen-fixture.txt"), Is.True);
            var problems = project.Services.GetRequiredService<ProblemsPanelViewModel>();
            factory.OpenPanel(EditorDockPanelIds.Problems); factory.ToggleGlobalPanel(EditorDockPanelIds.Problems); factory.OpenPanel(EditorDockPanelIds.Problems);
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var state = project.Services.GetRequiredService<EntryEditSession>().Get(entry, entry.DetailForm!.Fields.Single());
            state.Update("invalid", true, new GalNet.Core.Parameters.ValueIssue("value.type", "invalid"), false);
            Assert.That(problems.Problems.Any(problem => problem.Target?.EntryId == entry.StableId), Is.True);
            state.Clear();
            factory.CloseDockable(TimelineDockables(root).Single(item => item.Context is InspectorHostViewModel));
            factory.ApplyWorkspacePreset("Writing");
            Assert.That(TimelineDockables(root).Any(item => item.Context is InspectorHostViewModel), Is.True);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxRealViewsMountAtSmallSizesAndIconThemesKeepKeyboardAndCheckedFeedback()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            var app = Application.Current!;
            var resourceBase = new Uri("avares://GalNet.Editor/");
            app.Resources.MergedDictionaries.Add(new ResourceInclude(resourceBase) { Source = new Uri("avares://GalNet.Editor/Assets/icons.axaml") });
            app.Resources.MergedDictionaries.Add(new ResourceInclude(resourceBase) { Source = new Uri("avares://GalNet.Editor/Themes/EditorButtons.axaml") });
            app.Styles.Add(new StyleInclude(resourceBase) { Source = new Uri("avares://GalNet.Editor/Themes/EditorControlStyles.axaml") });
            foreach (var key in new[] { "AddIcon", "DeleteRegularIcon", "ChevronUpRegular", "ChevronDownRegular" }) app.Resources.Remove(key);
            foreach (var (theme, name) in new[] { (EditorThemeVariant.Violet, "Violet"), (EditorThemeVariant.Aurora, "Aurora") })
            {
                var resources = new ResourceDictionary();
                resources.MergedDictionaries.Add(new ResourceInclude(resourceBase) { Source = new Uri("avares://Semi.Avalonia/Themes/Dark/_index.axaml") });
                resources.MergedDictionaries.Add(new ResourceInclude(resourceBase) { Source = new Uri($"avares://GalNet.Editor/Themes/{name}Theme.axaml") });
                app.Resources.ThemeDictionaries[theme] = resources;
            }
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxViews"), "Views", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id); page.SelectedEntry = entry;
            using var inspector = new EntryInspectorViewModel(page);
            var groupView = new GroupEditorPanelView { DataContext = page };
            var details = new EntryInspectorView { DataContext = inspector };
            var content = new Grid { ColumnDefinitions = new("72*,28*") }; content.Children.Add(groupView); Grid.SetColumn(details, 1); content.Children.Add(details);
            var window = new Window { Content = content, Width = 1200, Height = 800 }; window.Show(); TimelinePump(window);
            void Capture(string name)
            {
                // Headless rendering has no continuous desktop frame clock. Capture settled endpoints;
                // transition intermediates and reversal are verified independently in EditorUiuxTests.
                foreach (var animated in window.GetVisualDescendants().OfType<Avalonia.Animation.Animatable>()) animated.Transitions = null;
                TimelinePump(window);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } path)
                { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(path, name)); }
            }
            try
            {
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark, EditorThemeVariant.Violet, EditorThemeVariant.Aurora })
                {
                    foreach (var animated in window.GetVisualDescendants().OfType<Avalonia.Animation.Animatable>()) animated.ClearValue(Avalonia.Animation.Animatable.TransitionsProperty);
                    app.RequestedThemeVariant = theme; window.RequestedThemeVariant = theme;
                    await Task.Delay(200); TimelinePump(window);
                    var add = groupView.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, page.ExpandAllCommand));
                    Assert.That(add.Bounds.Width, Is.EqualTo(32)); Assert.That(add.Bounds.Height, Is.EqualTo(32));
                    var surface = add.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ButtonSurface");
                    Assert.That((surface.Background as ISolidColorBrush)?.Color.A ?? 0, Is.Zero, "Icon buttons keep a transparent normal surface.");
                    var entrySurface = groupView.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("debugEntry"));
                    Assert.That(entrySurface.Transitions?.Count ?? 0, Is.Zero, "Selecting an entry must not animate its card surface.");
                    foreach (var schema in groupView.GetVisualDescendants().OfType<SchemaForm>().Where(form => form.IsEffectivelyVisible))
                        foreach (var item in schema.Editors)
                        {
                            Assert.That(item.Control.IsEffectivelyVisible, Is.True);
                            Assert.That(item.Control.Bounds.Width, Is.GreaterThan(60)); Assert.That(item.Control.Bounds.Height, Is.GreaterThanOrEqualTo(24));
                        }
                    Assert.That(surface.Transitions?.Count ?? 0, Is.Zero);
                    var toggle = groupView.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ParametersToggle");
                    Assert.That(toggle.IsChecked, Is.True); Assert.That(toggle.Theme, Is.Not.Null);
                    add.Focus(); Assert.That(add.IsFocused, Is.True);
                    Capture("uiux-group-" + theme.Key + "-1200.png");
                }
                window.Width = 960; window.Height = 640; TimelinePump(window);
                Assert.That(groupView.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Contexts).Any(context => context.Field.Id == "duration"), Is.True);
                Capture("uiux-group-960.png");
                window.Content = null;
                var scaled = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.5, 1.5), Child = content };
                window.Width = 1440; window.Height = 960; window.Content = scaled; TimelinePump(window); Capture("uiux-group-scale150.png");
                window.Content = null; scaled.Child = null;
                var graph = new NodeGraphPanelView { DataContext = workspace }; window.Content = graph; window.Width = 960; window.Height = 640; TimelinePump(window);
                Assert.That(graph.FindControl<Slider>("GraphZoom")!.IsEffectivelyVisible, Is.True); Capture("uiux-graph-960.png");
                window.Content = null;
                var asset = project.Services.GetRequiredService<AssetPanelViewModel>(); var files = new AssetPanelView { DataContext = asset }; window.Content = files; TimelinePump(window);
                asset.IsListView = true; asset.TypeFilterIndex = 1; TimelinePump(window); Assert.That(files.FindControl<ComboBox>("FileTypeFilter")!.IsEffectivelyVisible, Is.True); Capture("uiux-files-960.png");
                window.Content = null;
                var timeline = project.Services.GetRequiredService<TimelinePanelViewModel>();
                var timelineEntry = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
                timelineEntry.Source.Parameters["timeline"] = TimelineFixture(); page.SelectedEntry = timelineEntry;
                var timelineView = new TimelinePanelView { DataContext = timeline }; window.Content = timelineView; TimelinePump(window);
                Assert.That(timelineView.GetVisualDescendants().OfType<GridSplitter>().Count(), Is.EqualTo(3));
                Assert.That(timelineView.TreeColumnWidth.Value, Is.EqualTo(320)); timelineView.TreeColumnWidth = new GridLength(260); TimelinePump(window);
                Assert.That(timelineView.FindControl<GalNet.Game.Controls.Timeline.TimelineCanvas>("TimelineKeys")!.Bounds.Width, Is.GreaterThan(300));
                Capture("uiux-timeline-960.png");
            }
            finally { window.Close(); page.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxLocalizedDraftsSynchronizeAndSaveAfterEveryViewCloses()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxText"), "Text", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Text).Single();
            var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var first = new DtoEditorHost { Item = entry.DetailForm!.Item, Registry = page.FieldEditors };
            var second = new DtoEditorHost { Item = entry.DetailForm.Item, Registry = page.FieldEditors };
            var panel = new StackPanel { Children = { first, second } }; var window = new Window { Content = panel, Width = 900, Height = 700 }; window.Show(); TimelinePump(window);
            window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") }); TimelinePump(window);
            var edits = project.Services.GetRequiredService<EntryEditSession>();
            try
            {
                var composingState = edits.Get(entry, entry.DetailForm!.Fields.Single(field => field.Id == "content"));
                using (composingState.RegisterGuard(() => false))
                {
                    Assert.That(edits.PendingCount, Is.EqualTo(1), "Composition is pending even before TextBox.Text changes.");
                    Assert.ThrowsAsync<InvalidDataException>(async () => await workspace.SaveAsync());
                    Assert.That(composingState.IsDirty, Is.False, "Blocking composition must not invent a text draft.");
                }
                var a = first.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "GameTextInput");
                var b = second.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "GameTextInput");
                a.Text = "First draft"; Assert.That(b.Text, Is.EqualTo("First draft"));
                a.Text = "最终正文"; Assert.That(b.Text, Is.EqualTo("最终正文"));
                var draft = edits.Pending.Single(); using (draft.State.RegisterGuard(() => false))
                    Assert.ThrowsAsync<InvalidDataException>(async () => await workspace.SaveAsync());
                window.Content = null; page.Dispose();
                await workspace.SaveAsync();
                Assert.That(edits.PendingCount, Is.Zero);
                Assert.That(project.Services.GetRequiredService<GameTextAuthoringService>().Read(entry.Source.Parameters["content"].GetString()!), Is.EqualTo("最终正文"));
            }
            finally { foreach (var draft in edits.Pending.ToArray()) draft.State.Clear(); window.Close(); page.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxCommonAndFullEditorsShareDraftsAcrossDockClosureAndSave()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxDrafts"), "Drafts", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.MoveLayer).Single();
            var session = project.Services.GetRequiredService<EntryEditSession>();
            var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var common = new DtoEditorHost { Item = entry.DetailForm!.Item, Registry = page.FieldEditors, DisplayMode = FieldDisplayMode.Common };
            var inspectorModel = new EntryInspectorViewModel(page); page.SelectedEntry = entry;
            var detail = new EntryInspectorView { DataContext = inspectorModel };
            var panel = new Grid { ColumnDefinitions = new("*,*") }; panel.Children.Add(common); Grid.SetColumn(detail, 1); panel.Children.Add(detail);
            var window = new Window { Content = panel, Width = 1100, Height = 760 }; window.Show(); TimelinePump(window);
            try
            {
                FieldEditorContext Duration(Control control) => control.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Contexts).Single(context => context.Field.Id == "duration");
                var compactContext = Duration(common); var fullContext = Duration(detail);
                Assert.That(common.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Contexts).Any(context => context.Field.Id == "transform"), Is.False);
                Assert.That(detail.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Contexts).Any(context => context.Field.Id == "transform"), Is.True);
                compactContext.Text = "invalid";
                Assert.That(fullContext.Text, Is.EqualTo("invalid")); Assert.That(session.PendingCount, Is.EqualTo(1));
                window.Content = null; page.Dispose(); inspectorModel.Dispose();
                Assert.That(session.PendingCount, Is.EqualTo(1));
                Assert.ThrowsAsync<InvalidDataException>(async () => await workspace.SaveAsync());
                page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
                common = new DtoEditorHost { Item = entry.DetailForm!.Item, Registry = page.FieldEditors, DisplayMode = FieldDisplayMode.Common };
                window.Content = common; TimelinePump(window); compactContext = Duration(common);
                Assert.That(compactContext.Text, Is.EqualTo("invalid"));
                compactContext.Text = "1.75";
                await workspace.SaveAsync();
                Assert.That(entry.Source.Parameters["duration"].GetSingle(), Is.EqualTo(1.75));
                Assert.That(session.PendingCount, Is.Zero);
                compactContext.Text = "2.25";
                entry.Source.Parameters["duration"] = JsonSerializer.SerializeToElement(3f);
                Assert.That(await compactContext.CommitTextAsync(), Is.False);
                Assert.That(entry.Source.Parameters["duration"].GetSingle(), Is.EqualTo(3));
                compactContext.RevertDraft();
            }
            finally { foreach (var draft in session.Pending.ToArray()) draft.State.Clear(); window.Close(); page.Dispose(); inspectorModel.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxPresetsRetainDocumentsSelectionAndHistoryAndRestoreMaximization()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxLayout"), "Layout", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            var factory = project.Services.GetRequiredService<EditorDockFactory>(); var layout = factory.CreateLayout(); factory.InitLayout(layout); factory.OpenGroupEditor(group);
            var page = TimelineDockables(layout).Select(item => item.Context).OfType<GroupEditorPanelViewModel>().Single(); page.SelectedEntry = entry;
            var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
            foreach (var preset in new[] { "Animation", "Debug", "Writing" })
            {
                factory.ApplyWorkspacePreset(preset);
                Assert.That(TimelineDockables(layout).Any(item => ReferenceEquals(item.Context, page)), Is.True);
                Assert.That(page.SelectedEntry, Is.SameAs(entry)); Assert.That(page.IsDisposed, Is.False);
            }
            Assert.That(history.CanUndo, Is.False);
            var original = layout.VisibleDockables!.ToArray();
            factory.ToggleMaximizeActive(); factory.ToggleMaximizeActive();
            Assert.That(layout.VisibleDockables, Is.EqualTo(original));
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task UiuxProblemsValidateUnsavedAuthorValuesAndKeepStableEntryNavigation()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "UiuxProblems"), "Problems", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            await workspace.SaveAsync();
            var factory = project.Services.GetRequiredService<EditorDockFactory>(); var layout = factory.CreateLayout(); factory.InitLayout(layout);
            entry.Source.Parameters["duration"] = JsonSerializer.SerializeToElement("invalid");
            var problems = project.Services.GetRequiredService<ProblemsPanelViewModel>(); problems.ValidateCommand.Execute(null);
            var issue = problems.Problems.First(problem => problem.Target?.EntryId == entry.StableId);
            Assert.That(issue.Target!.NodeId, Is.EqualTo(group.Id));
            problems.NavigateCommand.Execute(issue);
            var inspectorCount = TimelineDockables(layout).Count(item => item.Context is InspectorHostViewModel);
            problems.NavigateCommand.Execute(issue);
            Assert.That(TimelineDockables(layout).Count(item => item.Context is InspectorHostViewModel), Is.EqualTo(inspectorCount), "Repeated navigation reuses the unlocked inspector.");
            Assert.That(project.Services.GetRequiredService<EntrySelectionContext>().Current?.EntryId, Is.EqualTo(entry.StableId));
            Assert.That(entry.ProblemCount, Is.GreaterThan(0));
            entry.Source.Parameters["duration"] = JsonSerializer.SerializeToElement(1f);
            problems.ValidateCommand.Execute(null);
            Assert.That(problems.Problems.Any(problem => problem.Target?.EntryId == entry.StableId), Is.False);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
