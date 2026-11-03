using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Debugging;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Presentation.Abstractions.View;
using GalNet.Runtime.Engine;
using GalNet.Editor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task ExternalDebugPanelConnectsThroughTcpEditsRuntimeValuesAndKeepsAuthorHistoryClean()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ExternalDebug"), "Debug", new(), Contracts());
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var authored = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.Text).ToArray();
            var entry = authored[0];
            var future = authored[1];
            await SetGameText(project, group.Id, entry.Source, "Test line");
            await SetGameText(project, group.Id, future.Source, "Future line");
            var panel = project.Services.GetRequiredService<ExternalDebugPanelViewModel>();
            var view = new ExternalDebugPanelView { DataContext = panel };
            var window = new Window { Content = view, Width = 1200, Height = 700 };
            using var groupEditor = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var entries = new EntryListEditorControl { DataContext = groupEditor };
            var graphNode = new GraphNodeControl { DataContext = group };
            var groupLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            groupLayout.Children.Add(graphNode); Grid.SetRow(entries, 1); groupLayout.Children.Add(entries);
            var groupWindow = new Window { Content = groupLayout, Width = 1000, Height = 700 };
            groupWindow.Show();
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                await panel.WaitForApplicationCommand.ExecuteAsync(null);
                Assert.That(panel.Error, Is.Empty);
                var client = project.Services.GetRequiredService<ExternalGameDebugClient>();
                var hotReload = project.Services.GetRequiredService<DebugContentHotReloadService>();
                Assert.That(client.Invitation, Is.Not.Null);
                using var gameView = new GameRegistration().UseCatalog(project.Services.GetRequiredService<IEntryCatalog>())
                    .RegisterInstruction(BuiltinEntryIds.Text, _ => new DebugTextWait()).BuildView();
                var graph = new Graph
                {
                    RootNodeId = group.Id,
                    Nodes = { new Group { Id = group.Id, Entries = { new PrimitiveEntry(BuiltinEntryIds.Text,
                    JsonSerializer.SerializeToElement(new { content = "Test line" })) { StableId = entry.StableId + "#1" },
                    new PrimitiveEntry(BuiltinEntryIds.Text, JsonSerializer.SerializeToElement(new { content = "Future line" }))
                    { StableId = future.StableId + "#1" } } } }
                };
                await using var engine = new GameEngine(graph, gameView);
                engine.Runtime.SetVariable("save.score", 4);
                engine.Runtime.SetVariable("player.unlocked", false);
                var revisions = new TestContentRevisions(graph);
                using var adapter = new EngineDebugRun(engine,
                    new() { Instructions = new Dictionary<string, DebugReplayBehavior> { [BuiltinEntryIds.Text] = DebugReplayBehavior.Scene } },
                    content: revisions);
                await using var host = new GameDebugHost();
                using var binding = host.AttachRun(adapter);
                await host.ConnectAsync(client.Invitation!);
                await engine.AdvanceAsync();
                async Task Until(Func<bool> condition)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    while (!condition()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(15, timeout.Token); }
                    Dispatcher.UIThread.RunJobs();
                }
                await Until(() => panel.SaveVariables.Count == 1 && panel.PlayerVariables.Count == 1);
                var toolbar = view.FindControl<WrapPanel>("DebugToolbar")!;
                var launch = view.FindControl<Button>("LaunchDebugApp")!;
                var stop = view.FindControl<Button>("StopDebugGame")!;
                Assert.That(toolbar.GetVisualDescendants().OfType<Button>().Count(), Is.EqualTo(11));
                Assert.That(launch.Content, Is.TypeOf<PathIcon>());
                Assert.That(ToolTip.GetTip(launch), Is.Not.Null);
                Assert.That(stop.IsEffectivelyEnabled, Is.True);
                view.FindControl<TabControl>("DebugTabs")!.SelectedIndex = 1;
                foreach (var width in new[] { 440, 320 })
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = width == 440 ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                    window.Width = width; window.Height = 650; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    Assert.That(view.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width));
                    Assert.That(view.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == panel.L["Debug.NoVariables"]), Is.False);
                    var footer = view.FindControl<Border>("DebugStatusBar")!;
                    Assert.That(footer.TranslatePoint(default, view)!.Value.Y, Is.GreaterThan(view.Bounds.Height - 80));
                    Assert.That(toolbar.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width));
                    foreach (var input in view.GetVisualDescendants().OfType<TextBox>().Where(input => input.IsEffectivelyVisible))
                    {
                        var point = input.TranslatePoint(default, view)!.Value;
                        Assert.That(point.X + input.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width + 1));
                    }
                    if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                    { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, $"debug-panel-{width}.png")); }
                }
                Click(window, toolbar.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, panel.PauseCommand)));
                await Until(() => panel.RuntimeState?.Paused == true && !panel.Busy);
                Click(window, toolbar.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, panel.ResumeCommand)));
                await Until(() => panel.RuntimeState?.Paused == false && !panel.Busy);
                Assert.That(group.IsDebugCurrent, Is.True);
                Assert.That(entry.IsDebugCurrent, Is.True);
                var entryChrome = entries.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("debugEntry") && ReferenceEquals(border.DataContext, entry));
                Assert.That(entryChrome.BorderThickness, Is.EqualTo(new Avalonia.Thickness(3)));
                Assert.That(graphNode.FindControl<Border>("NodeChrome")!.BorderThickness, Is.EqualTo(new Avalonia.Thickness(3)));
                var row = panel.SaveVariables.Single();
                row.Draft = "9";
                await row.ApplyCommand.ExecuteAsync(null);
                Assert.That(panel.Error, Is.Empty);
                await Until(() => row.CurrentValue == "9");
                Assert.That(engine.Runtime.GetVariable("score")!.AsInt(), Is.EqualTo(9));
                row.Draft = "12";
                engine.Runtime.SetVariable("save.score", 11);
                await Until(() => row.CurrentValue == "11");
                Assert.That(row.Draft, Is.EqualTo("12"));
                Assert.That(row.Conflict, Is.True);
                await row.ApplyCommand.ExecuteAsync(null);
                Assert.That(engine.Runtime.GetVariable("score")!.AsInt(), Is.EqualTo(11));
                Assert.That(panel.Error, Is.Not.Empty);
                Assert.That(panel.HasError, Is.True);
                Dispatcher.UIThread.RunJobs();
                Assert.That(view.FindControl<Border>("DebugErrorDetails")!.IsVisible, Is.True);
                Assert.That(project.Services.GetRequiredService<EditorHistories>().Content.IsDirty, Is.False);
                Assert.That(view.GetVisualDescendants().OfType<TabControl>().Single().Items.Count, Is.EqualTo(3));
                var initialBuild = hotReload.PublishedBuild;
                future.Condition = "true";
                await panel.HotReloadCommand.ExecuteAsync(null);
                Assert.Multiple(() =>
                {
                    Assert.That(panel.Error, Is.Empty);
                    Assert.That(hotReload.PublishedBuild, Is.Not.SameAs(initialBuild));
                    Assert.That(panel.HasPendingContent, Is.False);
                    Assert.That(revisions.InstallCount, Is.EqualTo(1));
                    Assert.That(revisions.ActivationCount, Is.EqualTo(1));
                });

                entry.Condition = "true";
                var directlyApplied = hotReload.PublishedBuild;
                await panel.HotReloadCommand.ExecuteAsync(null);
                Assert.Multiple(() =>
                {
                    Assert.That(panel.Error, Is.Empty);
                    Assert.That(panel.HasPendingContent, Is.True);
                    Assert.That(panel.CanApplyPendingContent, Is.True);
                    Assert.That(hotReload.PublishedBuild, Is.SameAs(directlyApplied), "Source maps must stay on the running content while an update is pending.");
                    Assert.That(revisions.InstallCount, Is.EqualTo(2));
                    Assert.That(revisions.ActivationCount, Is.EqualTo(1));
                });
                await panel.ApplyPendingContentCommand.ExecuteAsync(null);
                Assert.Multiple(() =>
                {
                    Assert.That(panel.Error, Is.Empty);
                    Assert.That(panel.HasPendingContent, Is.False);
                    Assert.That(hotReload.PublishedBuild, Is.Not.SameAs(directlyApplied));
                    Assert.That(revisions.ActivationCount, Is.EqualTo(2));
                });
                await panel.SeekAsync(group.Id, entry.StableId);
                Assert.That(engine.EntryIndex, Is.Zero);
                Assert.That(engine.Runtime.GetVariable("score")!.AsInt(), Is.EqualTo(4),
                    "Returning to the Group entrance restores its cached initial state.");
                await Until(() => panel.RuntimeState?.Paused == true && client.Host?.Run?.Generation == adapter.Info.Generation && !panel.Busy);
                await panel.ToggleBreakpointAsync(group.Id, entry.StableId);
                Assert.That(panel.Error, Is.Empty);
                Assert.That(entry.IsDebugBreakpoint, Is.True);
                await panel.StepCommand.ExecuteAsync(null);
                Assert.That(panel.Error, Is.Empty);
                Assert.That(engine.EntryIndex, Is.EqualTo(1));
                Assert.That(panel.HasError, Is.False);
                Assert.That(project.Services.GetRequiredService<EditorHistories>().Content.IsDirty, Is.False);
                await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 200);
                var beforeRestartRequirement = hotReload.PublishedBuild;
                await panel.HotReloadCommand.ExecuteAsync(null);
                Assert.Multiple(() =>
                {
                    Assert.That(panel.Error, Is.Empty);
                    Assert.That(panel.HasPendingContent, Is.True);
                    Assert.That(panel.CanRestartForContent, Is.True);
                    Assert.That(hotReload.PublishedBuild, Is.SameAs(beforeRestartRequirement));
                    Assert.That(revisions.InstallCount, Is.EqualTo(3));
                    Assert.That(revisions.ActivationCount, Is.EqualTo(2));
                });
                await panel.DisconnectCommand.ExecuteAsync(null);
                await Until(() => !panel.HasRun);
            }
            finally { groupWindow.Close(); window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
    private sealed class DebugTextWait : PrimitiveInstance
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        protected override void OnDispatch() { }
        protected override void OnPlaybackModeChanged(PrimitiveInvocationMode mode) { if (mode == PrimitiveInvocationMode.CatchUp) TryComplete(); }
    }

    private sealed class TestContentRevisions(Graph graph) : IDebugContentController
    {
        private Graph _latestGraph = graph;
        public int InstallCount { get; private set; }
        public int ActivationCount { get; private set; }

        public Task InstallAsync(DebugContentInstallRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = DebugContentManifestStore.Read(request.Directory);
            if (manifest.ContentFingerprint != request.ContentFingerprint) throw new InvalidDataException("Content fingerprint mismatch.");
            _latestGraph = new Graph
            {
                RootNodeId = graph.RootNodeId,
                Nodes = manifest.Groups.Select(group => (Node)new Group
                {
                    Id = group.Id,
                    Entries = group.Entries.SelectMany(source => source.PrimitiveIds.Select(id =>
                        (GalNet.Core.Entry.Entry)new PrimitiveEntry(BuiltinEntryIds.Text,
                            JsonSerializer.SerializeToElement(new { content = id })) { StableId = id })).ToList()
                }).ToList(),
                Edges = graph.Edges
            };
            InstallCount++;
            return Task.CompletedTask;
        }

        public Graph GetLatestGraph() => _latestGraph;

        public void CommitActivation() => ActivationCount++;
    }
}
