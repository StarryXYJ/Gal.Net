using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Models;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task MountedEntryResourceFollowsIdAcrossRenameMoveUndoAndReopen()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ResourceIdentity"), "Identity", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var files = scope.GetRequiredService<ProjectFileAuthoringService>(); var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/voice.ogg", Content: [1]));
            await files.ExecuteAsync(new(ProjectFileAction.CreateDirectory, [], "Assets/voice-folder"));
            var id = scope.GetRequiredService<IAssetCatalogService>().GetEntry("voice.ogg")!.Id!;
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.PlayAudio).Single();
            scope.GetRequiredService<EntryFieldAuthoringService>().SetParameters(group.Id, entry.Source, entry.Type,
                entry.Source.Parameters.Append(new KeyValuePair<string, JsonElement>("asset", JsonSerializer.SerializeToElement(id))).GroupBy(pair => pair.Key).ToDictionary(items => items.Key, items => items.Last().Value));
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            var view = new EntryListEditorControl { DataContext = page }; var window = new Window { Content = view, Width = 1200, Height = 900 }; window.Show(); TimelinePump(window);
            try
            {
                var picker = view.GetVisualDescendants().OfType<AssetPicker>().Single(control => control.Filter == AssetPickerFilter.Audio && control.IsEffectivelyVisible);
                await WaitForResourceUi(window, () => picker.DisplayText == "voice.ogg");
                var button = picker.FindControl<Button>("PickerButton")!; TimelineClick(window, button);
                await WaitForResourceUi(window, () => picker.ResourcePanel.Results.Any(item => item.Id == id));
                picker.ResourcePanel.Search.Text = "voice";
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                await files.ExecuteAsync(new(ProjectFileAction.Move, ["Assets/voice.ogg"], "Assets/voice-folder/voice-new.ogg", false));
                await WaitForResourceUi(window, () => picker.DisplayText == "voice-new.ogg" && picker.ResourcePanel.Results.Any(item => item.Id == id && item.Detail == "voice-folder/voice-new.ogg"));
                Assert.That(picker.SelectedAssetId, Is.EqualTo(id)); Assert.That(entry.Source.Parameters["asset"].GetString(), Is.EqualTo(id));
                Assert.That(picker.ResourcePanel.Search.Text, Is.EqualTo("voice"));
                await history.UndoAsync(); await WaitForResourceUi(window, () => picker.DisplayText == "voice.ogg");
                await history.RedoAsync(); await WaitForResourceUi(window, () => picker.DisplayText == "voice-new.ogg");
                Assert.That((await scope.GetRequiredService<IAssetManager>().GetFileAsync(id))!.Path, Is.EqualTo("voice-folder/voice-new.ogg"));
                Assert.That(await scope.GetRequiredService<IAssetManager>().GetFileAsync("voice-folder/voice-new.ogg"), Is.Null, "Paths never act as fallback identities.");
                ((Flyout)button.Flyout!).Hide(); await workspace.SaveAsync(); var root = project.RootPath; window.Close(); await projects.CloseAsync();
                project = await projects.OpenAsync(root);
                Assert.That((await project.Services.GetRequiredService<IAssetManager>().GetFileAsync(id))!.Path, Is.EqualTo("voice-folder/voice-new.ogg"));
                Assert.That(project.Services.GetRequiredService<IEditorDocumentService>().Groups[group.Id].Entries.Single().Parameters["asset"].GetString(), Is.EqualTo(id));
                await projects.CloseAsync(); return true;
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OpenVariantBufferRebasesFolderAndPreservesSavedAndUnsavedChanges(bool moveImages)
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "VariantBuffer"), "Variants", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var files = scope.GetRequiredService<ProjectFileAuthoringService>(); var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            await CreateVariantImages(project.RootPath, files);
            await files.ExecuteAsync(new(ProjectFileAction.CreateDirectory, [], "Assets/atlases"));
            await files.ExecuteAsync(new(ProjectFileAction.CreateFile, [], "Assets/hero.variants", Content: Encoding.UTF8.GetBytes("""{"folder":"alice","default":"idle_coat"}""")));
            var buffer = scope.GetRequiredService<ProjectTextFiles>().Open("Assets/hero.variants");
            using var inspector = new ImageVariantsInspector(project.RootPath, buffer, projects, scope.GetRequiredService<IResourceTypeCatalog>(), scope.GetRequiredService<IAssetCatalogService>(), scope.GetRequiredService<IEditorLocalizationService>());
            inspector.Default = "smile_skirt"; await inspector.ConfigurationSaving; // Unsaved author state must remain unsaved after moving.
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            await files.ExecuteAsync(moveImages ? new(ProjectFileAction.Move, ["Assets/alice"], "Assets/pictures", false)
                : new(ProjectFileAction.Move, ["Assets/hero.variants"], "Assets/atlases/hero.variants", false));
            var folder = moveImages ? "pictures" : "../alice";
            Assert.That(JsonDocument.Parse(buffer.Text).RootElement.GetProperty("folder").GetString(), Is.EqualTo(folder));
            Assert.That(inspector.Folder, Is.EqualTo(folder)); Assert.That(inspector.Default, Is.EqualTo("smile_skirt"));
            Assert.That(JsonDocument.Parse(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path))).RootElement.GetProperty("default").GetString(), Is.EqualTo("idle_coat"));
            await workspace.SaveAsync(); await history.UndoAsync();
            Assert.That(buffer.Path, Is.EqualTo("Assets/hero.variants")); Assert.That(inspector.Folder, Is.EqualTo("alice"));
            Assert.That(inspector.Default, Is.EqualTo("smile_skirt"));
            Assert.That(JsonDocument.Parse(File.ReadAllText(Path.Combine(project.RootPath, buffer.Path))).RootElement.GetProperty("default").GetString(), Is.EqualTo("smile_skirt"));
            Assert.That(scope.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
            await history.RedoAsync(); Assert.That(inspector.Folder, Is.EqualTo(folder));
            var id = scope.GetRequiredService<IAssetCatalogService>().GetEntry(buffer.Path[7..])!.Id!;
            Assert.That(await scope.GetRequiredService<IAssetManager>().GetFileAsync(id), Is.Not.Null);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task ResourceIndexSharesQueriesAndCancellationAndRefreshKeepsActiveLeasesAndRegistrations()
    {
        using var services = Services(); var manager = services.GetRequiredService<EditorAssetManager>();
        var provider = new ResourceTestProvider(); manager.RegisterProvider(provider);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); provider.Gate = gate.Task;
        using var cancellation = new CancellationTokenSource();
        var cancelled = manager.GetFilesAsync(ct: cancellation.Token); var first = manager.GetFileAsync("stable"); var second = manager.GetFilesAsync("audio");
        cancellation.Cancel(); Assert.CatchAsync<OperationCanceledException>(async () => await cancelled);
        gate.SetResult(); Assert.That(await first, Is.Not.Null); Assert.That(await second, Has.Count.EqualTo(1)); Assert.That(provider.Opens, Is.EqualTo(1));
        Assert.That(await manager.GetFileAsync("voice.ogg"), Is.Null); Assert.That(provider.Opens, Is.EqualTo(1));
        var decoder = new ResourceTestDecoder(); manager.RegisterDecoder("audio", decoder);
        using var lease = await manager.AcquireAsync<ResourceDecoded>("stable"); Assert.That(lease, Is.Not.Null);
        manager.RegisterProvider(new ResourceTestProvider("other")); // Refresh retires the old generation while its lease is alive.
        Assert.That(lease!.Value.Disposed, Is.False);
        using var next = await manager.AcquireAsync<ResourceDecoded>("stable"); Assert.That(next, Is.Not.Null); Assert.That(decoder.Decodes, Is.EqualTo(2));
        lease.Dispose(); Assert.That(lease.Value.Disposed, Is.True); Assert.That(next!.Value.Disposed, Is.False);
        Assert.That(await manager.GetFilesAsync(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ResourceIndexReportsDuplicateIdsAndReadFailuresInsteadOfMissingAndRetriesFailures()
    {
        using var services = Services(); var manager = services.GetRequiredService<EditorAssetManager>(); var provider = new ResourceTestProvider();
        manager.RegisterProvider(provider); provider.Failure = new IOException("Cannot read metadata");
        Assert.ThrowsAsync<IOException>(async () => await manager.GetFileAsync("stable"));
        provider.Failure = null; Assert.That(await manager.GetFileAsync("stable"), Is.Not.Null); Assert.That(provider.Opens, Is.EqualTo(2));
        manager.RegisterProvider(new ResourceTestProvider());
        Assert.ThrowsAsync<InvalidDataException>(async () => await manager.GetFilesAsync());
    }

    [Test]
    public async Task PickerNeverRebindsToReplacementAtSamePathAndDistinguishesReadErrors()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var manager = services.GetRequiredService<EditorAssetManager>();
            var provider = new ResourceTestProvider(); manager.RegisterProvider(provider);
            using var picker = new AssetPicker { AssetManager = manager, SelectedAssetId = "stable", Filter = AssetPickerFilter.Audio };
            var window = new Window { Content = picker, Width = 650, Height = 600 }; window.Show();
            try
            {
                await WaitForResourceUi(window, () => picker.DisplayText == "voice.ogg");
                provider.Id = "replacement"; manager.RegisterProvider(new ResourceTestProvider("other"));
                await WaitForResourceUi(window, () => picker.DisplayText == "Missing!");
                Assert.That(picker.SelectedAssetId, Is.EqualTo("stable")); Assert.That(picker.DetailPath, Is.EqualTo("stable"));
                provider.Id = "stable"; provider.Failure = new IOException("Metadata is unreadable"); manager.RegisterProvider(new ResourceTestProvider("third"));
                await WaitForResourceUi(window, () => picker.DisplayText == "Unable to load");
                Assert.That(picker.DetailPath, Does.Contain("Metadata is unreadable")); Assert.That(picker.SelectedAssetId, Is.EqualTo("stable"));
                provider.Failure = null; manager.RegisterProvider(new ResourceTestProvider("fourth"));
                await WaitForResourceUi(window, () => picker.DisplayText == "voice.ogg");
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task PickerDropsOldLoadsAndCachesThumbnailsUntilResourceGenerationChanges()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var manager = services.GetRequiredService<EditorAssetManager>(); var provider = new ResourceTestProvider(); manager.RegisterProvider(provider);
            using (var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            {
                using var bytes = new MemoryStream(); bitmap.Save(bytes); provider.Data = bytes.ToArray();
            }
            provider.TypeId = "sprite";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); provider.Gate = gate.Task;
            using var picker = new AssetPicker { AssetManager = manager, SelectedAssetId = "stable", Filter = AssetPickerFilter.Image };
            var window = new Window { Content = picker, Width = 650, Height = 600 }; window.Show(); TimelinePump(window);
            try
            {
                var button = picker.FindControl<Button>("PickerButton")!; TimelineClick(window, button);
                provider.Gate = null; provider.Path = "new-name.png"; manager.RegisterProvider(new ResourceTestProvider("other"));
                await WaitForResourceUi(window, () => picker.DisplayText == "new-name.png" && picker.Items.Any(item => item.File?.Id == "stable"));
                var item = picker.Items.Single(item => item.File?.Id == "stable"); gate.SetResult();
                Assert.That(item.Thumbnail, Is.Not.Null); Assert.That(item.Thumbnail!.PixelSize.Width, Is.EqualTo(128));
                ((Flyout)button.Flyout!).Hide(); TimelineClick(window, button);
                await WaitForResourceUi(window, () => picker.Items.Any(candidate => candidate.File?.Id == "stable"));
                Assert.That(picker.Items.Single(candidate => candidate.File?.Id == "stable"), Is.SameAs(item));
                Assert.That(picker.DisplayText, Is.EqualTo("new-name.png")); Assert.That(picker.SelectedAssetId, Is.EqualTo("stable"));
                provider.Path = "latest.png"; manager.RegisterProvider(new ResourceTestProvider("third"));
                await WaitForResourceUi(window, () => picker.DisplayText == "latest.png" && picker.Items.Any(candidate => candidate.File?.Id == "stable" && candidate.Path == "latest.png"));
                Assert.That(picker.Items.Single(candidate => candidate.File?.Id == "stable").Thumbnail, Is.Not.SameAs(item.Thumbnail));
                window.Close(); return true;
            }
            finally { gate.TrySetResult(); window.Close(); }
        }, CancellationToken.None);
    }

    private static async Task WaitForResourceUi(Window window, Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < until) { await Task.Delay(10); TimelinePump(window); }
        Assert.That(ready(), Is.True, "Resource UI did not reach the expected generation.");
    }
    private sealed class ResourceTestProvider(string id = "stable") : IAssetProvider, IDisposable
    {
        public string Name => "Resource tests";
        public string Id { get; set; } = id;
        public string Path { get; set; } = "voice.ogg";
        public string TypeId { get; set; } = "audio";
        public byte[] Data { get; set; } = [1];
        public Task? Gate { get; set; }
        public Exception? Failure { get; set; }
        public int Opens { get; private set; }
        public bool Disposed { get; private set; }
        public bool Exists(string name) => true;
        public IArchive OpenArchive(string name) => OpenArchiveAsync(name).GetAwaiter().GetResult();
        public async Task<IArchive> OpenArchiveAsync(string name, CancellationToken ct = default)
        {
            Opens++; var path = Path;
            if (Gate is { } gate) await gate.WaitAsync(ct);
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (Failure is { } failure) throw failure;
            return new ResourceTestArchive(new GameFile(Id, path, TypeId, new AssetMeta { Id = Id, Path = path, TypeId = TypeId }, Data));
        }
        public void Dispose() => Disposed = true;
    }
    private sealed class ResourceTestArchive(IGameFile file) : IArchive
    {
        public string Name => "assets";
        public IEnumerable<string> AssetIds => [file.Id];
        public bool Contains(string id) => id == file.Id;
        public IGameFile? GetAsset(string id) => Contains(id) ? file : null;
        public IGameFile? GetAssetByPath(string path) => file.Path == path ? file : null;
        public void Dispose() { }
    }
    private sealed class ResourceDecoded : IDisposable { public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class ResourceTestDecoder : IAssetDecoder<ResourceDecoded>
    {
        public int Decodes { get; private set; }
        public ValueTask<ResourceDecoded?> DecodeAsync(IGameFile file, ReadOnlyMemory<byte> data, CancellationToken ct = default) { Decodes++; return ValueTask.FromResult<ResourceDecoded?>(new()); }
    }
}
