using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Presentation.Abstractions.Application;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class GalleryMechanismTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task PendingThumbnailCannotPublishAfterReplacementOrDisposal(bool replace) => OnUi(async () =>
    {
        using var media = new GalleryMediaService(new Session());
        using var thumbnails = new GalleryThumbnailCollection(media);
        var result = new TaskCompletionSource<Bitmap?>();
        CancellationToken pendingToken = default;
        var loading = thumbnails.ActivateAsync(Gallery(1), (_, token) =>
        {
            pendingToken = token;
            return result.Task; // Simulates a decoder that finishes after cancellation.
        }, default);
        var oldRow = thumbnails.Items.Single();
        if (replace) await thumbnails.ActivateAsync(Gallery(2), (_, _) => Task.FromResult<Bitmap?>(null), default);
        else thumbnails.Dispose();
        result.SetResult(new RenderTargetBitmap(new PixelSize(8, 8)));
        await loading;
        Assert.That(pendingToken.IsCancellationRequested, Is.True);
        Assert.That(oldRow.Thumbnail, Is.Null);
        if (replace) Assert.That(thumbnails.Items.Single().Id, Is.EqualTo(2));
        else Assert.That(thumbnails.Items, Is.Empty);
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task SceneRunIsReleasedExactlyOnceWhenStartupFinishesAfterCancellation(bool disposeWhileStarting) => OnUi(async () =>
    {
        var session = new Session();
        var gameplay = new GamePresentationState();
        var surface = new GamePage { Presentation = gameplay };
        await using var replay = new GalleryScenePlayback(session, surface);
        using var cancellation = new CancellationTokenSource();
        var starting = replay.ActivateAsync(Gallery(1).Items[0], cancellation.Token);
        Task? disposal = null;
        if (disposeWhileStarting)
        {
            disposal = replay.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False, "Pending startup must drain before scope cleanup completes.");
        }
        else cancellation.Cancel();
        var run = new Run();
        session.Starting.SetResult(run);
        await starting;
        if (disposal is not null) await disposal;
        await replay.DisposeAsync();
        Assert.That(run.Releases, Is.EqualTo(1));
        Assert.That(replay.Surface, Is.SameAs(surface));
        Assert.That(replay.Gameplay, Is.SameAs(gameplay));
    });

    private static GalleryTypeData Gallery(int id) => new(
        new GalleryTypeRegistration { TypeId = "scene", ResourceTypeId = "scene" },
        [new(new GalleryItem { Id = id }, true)]);

    private static Task OnUi(Func<Task> action) => HeadlessUnitTestSession
        .GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly)
        .Dispatch(action, TestContext.CurrentContext.CancellationToken);

    private sealed class Run : IAsyncDisposable
    {
        public int Releases { get; private set; }
        public ValueTask DisposeAsync() { Releases++; return ValueTask.CompletedTask; }
    }

    private sealed class Session : IGameSessionService, ISceneGallerySession
    {
        public TaskCompletionSource<IAsyncDisposable> Starting { get; } = new();
        public Task<IAsyncDisposable> StartSceneAsync(GalleryItemData item, GamePresentationState gameplay,
            GamePage surface, Action completed, CancellationToken cancellationToken = default) => Starting.Task;
        public string GameTitle => "Gallery test";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => false;
        public bool CanContinue => false;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new ObservableCollection<GameSaveSlot>());
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
