using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Scene;
using GalNet.Core.Variants;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task ColdAtlasSelectorCanFinishPreparationButAnAuthorEditStillInvalidatesIt()
    {
        using var assets = new VariantsAssets(); using var services = VariantsServices(assets); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "ColdAtlas"), "Atlas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var show = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowVariants).Single(); show.Source.Parameters["assetId"] = JsonSerializer.SerializeToElement("atlas");
        var select = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.SelectVariants).Single();
        var gate = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = gate;
        var catalog = scope.GetRequiredService<SceneTargetCatalog>(); using var selection = new SceneTargetSelection(catalog, catalog.Query(group.Id, select.StableId));
        selection.Select(selection.Items.Single().Id); var preparing = selection.PrepareAsync();
        await assets.File.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); gate.SetResult(AtlasBytes()); await preparing;
        Assert.That(selection.TryResolve(out var handle), Is.True); Assert.That(handle, Is.EqualTo(show.Source.Parameters["handleId"].GetString()));
        scope.GetRequiredService<EntryFieldAuthoringService>().SetCondition(group.Id, show.Source, "false"); Assert.That(selection.TryResolve(out _), Is.False);
        await projects.CloseAsync();
    }
    private static byte[] AtlasBytes(string defaultKey = "idle_coat") => ImageVariantsCodec.Encode(ImageVariantsBuilder.Build(
        new() { Folder = ".", Default = defaultKey }, new[] { "idle_coat", "smile_skirt", "weap" }.Select(key => new ImageVariantFile(key + ".png", "sprite-" + key))).Definition!);

    private static ServiceProvider VariantsServices(VariantsAssets assets) => new ServiceCollection().AddEditorServices()
        .AddSingleton<IEditorLocalizationService>(new EditorLocalizationService()).AddSingleton<IProjectClosePrompt>(new DiscardProjectClosePrompt())
        .AddSingleton<IEditorSettingsService>(new TestSettings()).AddSingleton<IFileDialogService>(new TestDialogs())
        .AddSingleton<IEffectCatalog>(new EffectCatalog([])).AddSingleton<IAssetManager>(assets)
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Test]
    public async Task AtlasQueryLoadsCompiledDefinitionsAsynchronouslyAndRejectsOldResourceAndProjectGenerations()
    {
        using var assets = new VariantsAssets(); using var services = VariantsServices(assets); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "AtlasQuery"), "Atlas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var show = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowVariants).Single(); show.Source.Parameters["assetId"] = JsonSerializer.SerializeToElement("atlas");
        using var query = new ProjectSceneTargets(scope.GetRequiredService<IEditorDocumentService>(), assets);
        var old = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = old;
        Assert.That(query.Before(group.Id, null).Complete, Is.False);
        await assets.File.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var revision = query.Revision;
        using var cancel = new CancellationTokenSource(); var wait = query.PrepareAsync(cancel.Token); cancel.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await wait);
        Assert.That(query.Revision, Is.EqualTo(revision));
        var oldWait = query.PrepareAsync(); assets.File.Gate = null; assets.File.Bytes = AtlasBytes("weap"); query.InvalidateResources(); await query.PrepareAsync();
        var refreshed = query.Revision; Assert.That(query.Before(group.Id, null).Targets.Single().Resource, Is.EqualTo("sprite-weap"));
        old.SetResult(AtlasBytes()); await oldWait;
        Assert.That(query.Revision, Is.EqualTo(refreshed)); Assert.That(query.Before(group.Id, null).Targets.Single().Resource, Is.EqualTo("sprite-weap"));
        assets.File.Bytes = "{}"u8.ToArray(); query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False); var reads = assets.File.Reads;
        await query.PrepareAsync(); Assert.That(assets.File.Reads, Is.EqualTo(reads));
        assets.File.TypeId = "sprite"; query.InvalidateResources(); await query.PrepareAsync(); Assert.That(assets.File.Reads, Is.EqualTo(reads));
        assets.File.TypeId = ImageVariantsCodec.ResourceType; assets.File.DeclaredLength = 1024 * 1024 + 1;
        query.InvalidateResources(); await query.PrepareAsync(); Assert.That(assets.File.Reads, Is.EqualTo(reads));
        assets.File.DeclaredLength = null; assets.File.Bytes = [0xff]; query.InvalidateResources(); await query.PrepareAsync(); Assert.That(query.Before(group.Id, null).Complete, Is.False);
        var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = pending;
        query.InvalidateResources(); var closing = query.PrepareAsync(); query.Dispose(); var closedRevision = query.Revision;
        pending.SetResult(AtlasBytes()); await closing;
        Assert.That(query.Revision, Is.EqualTo(closedRevision)); Assert.That(query.Before(group.Id, null).Reachable, Is.False);
        Assert.That(assets.File.SynchronousReads, Is.Zero); await projects.CloseAsync();
    }

    [Test]
    public async Task MountedVariantsFieldsUseTypedAssetAndScenePickersSingleHistoryAndSaveReopen()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new VariantsAssets(); using var services = VariantsServices(assets);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AtlasFields"), "Atlas", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer);
            var show = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.ShowVariants).Single();
            var select = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.SelectVariants).Single();
            select.Source.Parameters["handleId"] = JsonSerializer.SerializeToElement("unknown");
            select.Source.Parameters["dimension"] = JsonSerializer.SerializeToElement("d1"); select.Source.Parameters["value"] = JsonSerializer.SerializeToElement("weap");
            select.Source.Parameters["future"] = JsonSerializer.Deserialize<JsonElement>("[9007199254740993,null]");
            foreach (var item in group.Entries) item.IsParametersExpanded = ReferenceEquals(item, show);
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = show;
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 1100 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(Guid.TryParse(show.Source.Parameters["handleId"].GetString(), out _), Is.True);
                Assert.That(show.ParameterForm!.Binding(show.ParameterForm.Fields.Single(field => field.Id == "handleId")).IsVisible, Is.False);
                var picker = view.GetVisualDescendants().OfType<AssetPicker>().Single(control => control.IsEffectivelyVisible && control.Filter == AssetPickerFilter.ImageVariants);
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                TimelineClick(window, picker.FindControl<Button>("PickerButton")!); TimelinePump(window);
                Assert.That(picker.ResourcePanel.Results.Select(item => item.Id), Is.EquivalentTo(new[] { "", "atlas" }));
                picker.ResourcePanel.Search.Text = "figure.variants"; TimelinePump(window); PressGroupKey(picker.ResourcePanel.Search, Key.Down); PressGroupKey(picker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                Assert.That(show.Source.Parameters["assetId"].GetString(), Is.EqualTo("atlas"));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync();
                await scope.GetRequiredService<ProjectSceneTargets>().PrepareAsync();
                show.IsParametersExpanded = false; select.IsParametersExpanded = true; page.SelectedEntry = select; TimelinePump(window); history.Clear();
                Assert.That(view.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Diagnostics), Is.Empty);
                var button = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SceneTargetField" && control.IsEffectivelyVisible);
                TimelineClick(window, button); var scenePicker = (SceneTargetPicker)((Flyout)button.Flyout!).Content!; TimelinePump(window);
                Assert.That(scenePicker.ResourcePanel.Results, Has.Count.EqualTo(2), "Only None and the atlas instance; ordinary layers are excluded.");
                scenePicker.ResourcePanel.Search.Text = scenePicker.ResourcePanel.Results.Single(item => item.Id != "none").Label; TimelinePump(window);
                PressGroupKey(scenePicker.ResourcePanel.Search, Key.Down); PressGroupKey(scenePicker.ResourcePanel.Search, Key.Enter); TimelinePump(window);
                var handle = show.Source.Parameters["handleId"].GetString(); Assert.That(select.Source.Parameters["handleId"].GetString(), Is.EqualTo(handle));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); Assert.That(select.Source.Parameters["handleId"].GetString(), Is.EqualTo("unknown")); await history.RedoAsync();
                var catalog = scope.GetRequiredService<SceneTargetCatalog>();
                using var stale = new SceneTargetSelection(catalog, catalog.Query(group.Id, select.StableId));
                stale.Select(stale.Items.First(item => item.Id != "none").Id);
                scope.GetRequiredService<ProjectSceneTargets>().InvalidateResources(); Assert.That(stale.TryResolve(out _), Is.False);
                var root = project.RootPath; var groupId = group.Id; var selectId = select.StableId;
                await workspace.SaveAsync(); window.Close(); page.Dispose(); await projects.CloseAsync();
                project = await projects.OpenAsync(root); var saved = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(entry => entry.StableId == selectId);
                Assert.That(saved.Source.Parameters["handleId"].GetString(), Is.EqualTo(handle));
                Assert.That(JsonElement.DeepEquals(saved.Source.Parameters["future"], JsonSerializer.Deserialize<JsonElement>("[9007199254740993,null]")), Is.True);
                await projects.CloseAsync(); return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class VariantsAssets : IAssetManager
    {
        public VariantsFile File { get; } = new();
        private readonly PickerFile _other = new("voice", "voice.ogg", "audio");
        public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => Task.FromResult<IGameFile?>(assetId == "atlas" ? File : null);
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IGameFile>>(new IGameFile[] { File, _other }.Where(file => typeId is null || typeId == file.TypeId).ToArray());
        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => throw new NotSupportedException();
        public void RegisterProvider(IAssetProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private sealed class VariantsFile : IGameFile
    {
        public byte[] Bytes { get; set; } = AtlasBytes();
        public TaskCompletionSource<byte[]>? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, SynchronousReads;
        public string Id => "atlas";
        public string Path => "figure.variants";
        public string TypeId { get; set; } = ImageVariantsCodec.ResourceType;
        public AssetMeta Metadata => new() { Id = Id, Path = Path, TypeId = TypeId };
        public long? DeclaredLength { get; set; }
        public long Length => DeclaredLength ?? Bytes.Length;
        public string? Hash => null;
        public Stream OpenRead() { Interlocked.Increment(ref SynchronousReads); throw new NotSupportedException(); }
        public byte[] ReadAllBytes() { Interlocked.Increment(ref SynchronousReads); throw new NotSupportedException(); }
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
        { Interlocked.Increment(ref Reads); var gate = Gate; Entered.TrySetResult(); return gate?.Task ?? Task.FromResult(Bytes); }
    }
}
