using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private const string EffectSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer,scenePost
        @param amount
          uniform: uAmount
          type: float
          default: 0.25
          range: 0..1
        */
        """;
    private static ServiceProvider EffectServices(MetadataAssets assets, MetadataResources resources) => new ServiceCollection().AddEditorServices()
        .AddSingleton<IEditorLocalizationService>(new EditorLocalizationService())
        .AddSingleton<IProjectClosePrompt>(new DiscardProjectClosePrompt())
        .AddSingleton<IEditorSettingsService>(new TestSettings()).AddSingleton<IFileDialogService>(new TestDialogs())
        .AddSingleton<IEffectCatalog>(new EffectCatalog([])).AddSingleton<IAssetManager>(assets)
        .AddSingleton<IAssetCatalogService>(resources).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Test]
    public async Task EffectMetadataLoadsOffQueryAndRefreshRejectsOldLeasesWithoutResurrectingDisposedProjects()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "MetadataLifetime"), "Metadata", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var fx = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single();
        fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
        using var assets = new MetadataAssets(); var old = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = old;
        using var query = new ProjectSceneTargets(scope.GetRequiredService<IEditorDocumentService>(), assets);
        var state = query.Before(group.Id, null); var revision = query.Revision;
        Assert.That(state.Complete, Is.False); Assert.That(state.Targets.Single().Properties.Select(property => property.Name), Is.EqualTo(new[] { "progress" }));
        await assets.File.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancelled = new CancellationTokenSource(); var wait = query.PrepareAsync(cancelled.Token); cancelled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await wait);
        Assert.That(query.Revision, Is.EqualTo(revision), "Cancelling one caller must not cancel the shared resource generation.");
        var firstPreparation = query.PrepareAsync();
        assets.File.Gate = null; assets.File.Source = EffectSource.Replace("amount", "replacement"); query.InvalidateResources();
        await query.PrepareAsync(); var refreshed = query.Revision;
        Assert.That(query.Before(group.Id, null).Targets.Single().Properties.Any(property => property.Name == "replacement"), Is.True);
        old.SetResult(Encoding.UTF8.GetBytes(EffectSource)); await firstPreparation;
        Assert.That(query.Before(group.Id, null).Targets.Single().Properties.Any(property => property.Name == "amount"), Is.False);
        Assert.That(query.Revision, Is.EqualTo(refreshed), "A superseded loader cannot publish into the refreshed generation.");
        assets.File.Source = "invalid metadata"; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False); var reads = assets.File.Reads;
        await query.PrepareAsync(); Assert.That(assets.File.Reads, Is.EqualTo(reads), "Negative results are cached until a resource refresh.");
        assets.File.Source = EffectSource; assets.File.TypeId = "sprite"; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(assets.File.Reads, Is.EqualTo(reads)); Assert.That(query.Before(group.Id, null).Complete, Is.False);
        assets.File.TypeId = "effect-program"; assets.File.DeclaredLength = 1024 * 1024 + 1; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(assets.File.Reads, Is.EqualTo(reads)); Assert.That(query.Before(group.Id, null).Complete, Is.False);
        assets.File.DeclaredLength = null; assets.File.Bytes = [0xff]; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False, "Malformed UTF-8 cannot publish declarations."); assets.File.Bytes = null;
        fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("missing"); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False); fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
        var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = pending;
        query.InvalidateResources(); var preparing = query.PrepareAsync(); query.Dispose(); var disposedRevision = query.Revision;
        pending.SetResult(Encoding.UTF8.GetBytes(EffectSource)); await preparing;
        Assert.That(query.Revision, Is.EqualTo(disposedRevision)); Assert.That(query.Before(group.Id, null).Reachable, Is.False);
        Assert.That(assets.SynchronousReads, Is.Zero); await projects.CloseAsync();
    }

    [Test]
    public async Task EffectMetadataTimelineUsesRealPropertyMenuHistoryRefreshAndSaveReopen()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new MetadataAssets(); using var resources = new MetadataResources();
            using var services = EffectServices(assets, resources); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "MetadataTimeline"), "Metadata", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var fx = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single(); var handle = fx.Source.Parameters["instanceId"].GetString();
            fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program"); fx.Source.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("{\"amount\":0.6,\"unknown\":[9007199254740993,null]}");
            var timeline = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
            timeline.Source.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("{\"durationFrames\":20,\"tracks\":[],\"events\":[]}");
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var query = scope.GetRequiredService<ProjectSceneTargets>(); await query.PrepareAsync();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = timeline;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var catalog = scope.GetRequiredService<SceneTargetCatalog>(); using var lease = new SceneTargetSelection(catalog, catalog.Query(group.Id, timeline.StableId));
            Assert.That(lease.Select(lease.Items.Single().Id), Is.True);
            var view = scope.GetRequiredService<GalNet.Editor.Views.TimelinePanelView>(); view.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var window = new Window { Content = view, Width = 1350, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(session.AnalysisIncomplete, Is.False);
                var draft = session.BeginObjectEdit()!; draft.Select(draft.Choices.Single().Id); Assert.That(draft.Commit(), Is.True);
                session.Frame = 10; TimelinePump(window);
                var plus = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "AddTimelineProperty" && control.IsVisible && control.DataContext is TimelineTreeNode node && node.ObjectHandle == handle);
                TimelineClick(window, plus); var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(plus)!;
                menu.Items.OfType<MenuItem>().Single(item => item.DataContext is TimelineValueRow row && row.Property == "amount").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Hide(); TimelinePump(window);
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(.6f));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); TimelinePump(window);
                var row = session.Rows.Single(item => item.HasTrack); var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => ReferenceEquals(control.DataContext, row));
                var text = input.GetVisualDescendants().OfType<TextBox>().Single(); text.Focus(); text.Text = "0.8"; TimelinePump(window); PressGroupKey(text, Key.Enter); TimelinePump(window);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); TimelinePump(window);
                var stale = session.Rows.Single(item => item.HasTrack); var saved = timeline.Source.Parameters["timeline"].Clone(); history.Clear();
                assets.File.Source = EffectSource.Replace("amount", "replacement"); resources.RaiseChanged(); TimelinePump(window); await query.PrepareAsync(); TimelinePump(window);
                Assert.That(lease.TryResolve(out _), Is.False); stale.DraftValue = .9m; Assert.That(stale.Commit(), Is.False);
                Assert.That(session.Rows.Any(item => item.Property == "replacement"), Is.True);
                Assert.That(query.Before(group.Id, timeline.StableId).Targets.Single().Properties.Any(property => property.Name == "amount"), Is.False);
                Assert.That(session.Rows.Single(item => item.HasTrack).Property, Is.EqualTo("amount"));
                Assert.That(JsonElement.DeepEquals(timeline.Source.Parameters["timeline"], saved), Is.True); Assert.That(history.CanUndo, Is.False);
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var entryId = timeline.StableId; var fxId = fx.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId);
                Assert.That(JsonElement.DeepEquals(reopened.Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"], saved), Is.True);
                Assert.That(reopened.Entries.Single(item => item.StableId == fxId).Source.Parameters["parameters"].GetProperty("unknown")[0].GetInt64(), Is.EqualTo(9007199254740993L));
                await project.Services.GetRequiredService<ProjectSceneTargets>().PrepareAsync();
                Assert.That(project.Services.GetRequiredService<ProjectSceneTargets>().Before(groupId, entryId).Targets.Single().Properties.Any(property => property.Name == "replacement"), Is.True);
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class MetadataAssets : IAssetManager
    {
        public MetadataFile File { get; } = new();
        public int SynchronousReads => File.SynchronousReads;
        public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => Task.FromResult<IGameFile?>(assetId == "program" ? File : null);
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IGameFile>>([File]);
        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => throw new NotSupportedException();
        public void RegisterProvider(IAssetProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private sealed class MetadataFile : IGameFile
    {
        public string Source { get; set; } = EffectSource;
        public TaskCompletionSource<byte[]>? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, SynchronousReads;
        public string Id => "program";
        public string Path => "Effects/test.sksl";
        public string TypeId { get; set; } = "effect-program";
        public AssetMeta Metadata => new() { Id = Id, Path = Path, TypeId = TypeId };
        public long? DeclaredLength { get; set; }
        public byte[]? Bytes { get; set; }
        public long Length => DeclaredLength ?? Source.Length;
        public string? Hash => null;
        public Stream OpenRead() { Interlocked.Increment(ref SynchronousReads); throw new NotSupportedException(); }
        public byte[] ReadAllBytes() { Interlocked.Increment(ref SynchronousReads); throw new NotSupportedException(); }
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
        { Interlocked.Increment(ref Reads); var gate = Gate; Entered.TrySetResult(); return gate?.Task ?? Task.FromResult(Bytes ?? Encoding.UTF8.GetBytes(Source)); }
    }
    private sealed class MetadataResources : IAssetCatalogService
    {
        public event Action? Changed;
        public void RaiseChanged() => Changed?.Invoke();
        public IReadOnlyList<AssetEntry> GetDirectory(string relativeDirectory) => [];
        public bool DirectoryExists(string relativeDirectory) => false;
        public AssetEntry? GetEntry(string relativePath) => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetThumbnailPathAsync(AssetEntry entry, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }
}
