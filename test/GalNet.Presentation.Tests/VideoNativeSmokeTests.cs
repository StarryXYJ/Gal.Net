using Avalonia;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Rendering.Scene;

namespace GeneralTest.Presentation;

public sealed class VideoNativeSmokeTests
{
    [TestCase(false), TestCase(true), Explicit("Requires a supplied video asset directory and native LibVLC path."), NonParallelizable]
    public async Task Real_video_decodes_into_scene_pixels_and_stops_cleanly(bool naturalEnd)
    {
        var assetsRoot = Environment.GetEnvironmentVariable("GALNET_VIDEO_ASSETS") ?? throw new InvalidOperationException("Set GALNET_VIDEO_ASSETS.");
        var nativePath = Environment.GetEnvironmentVariable("GALNET_VIDEO_LIBVLC") ?? throw new InvalidOperationException("Set GALNET_VIDEO_LIBVLC.");
        LibVLCSharp.Shared.Core.Initialize(nativePath);
        using var probeVlc = new LibVLCSharp.Shared.LibVLC("--no-video-title-show");
        using var probe = new LibVLCSharp.Shared.Media(probeVlc, Path.Combine(assetsRoot, "niko.mp4"), LibVLCSharp.Shared.FromType.FromPath);
        await probe.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseLocal).WaitAsync(TimeSpan.FromSeconds(5));
        var duration = probe.Duration / 1000d;
        Assert.That(duration, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Video duration: {duration} seconds.");
        using var assets = new AssetManager([new LocalFileProvider(assetsRoot)]);
        var state = new GamePageViewModel(new NoOpGameNavigationService());
        await using var presenter = new AvaloniaVideoPresenter(assets, state, new ImmediateDispatcher());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var request = new VideoRenderRequest("native-smoke", new LayerRenderRequest("video", "a1000000000000000000000000000011",
            new LayerTransform(), 5, LayerDisplayMode.Uniform, 1), naturalEnd ? [Math.Max(.1, duration - .6)] : [1, 2, 999999]);
        var playing = presenter.PlayVideoAsync(request, cancellation.Token);
        byte[]? capture = null;
        for (var attempt = 0; attempt < 300; attempt++)
        {
            if (playing.IsCompleted) await playing;
            var layer = state.Layers.FirstOrDefault();
            if (layer?.FrameSource is { } frames)
            {
                using var frame = frames.AcquireFrame();
                if (frame.Texture?.SkBitmap is { } bitmap)
                {
                    TestContext.Out.WriteLine($"Decoded {bitmap.Width}x{bitmap.Height} video frame.");
                    Assert.That(bitmap.Width, Is.GreaterThan(0));
                    using var scene = SceneRenderPipeline.Render(SceneRenderPlan.Create([new SceneRenderEntry(layer, 0)]), [], null, new Size(320, 180));
                    using var image = SkiaSharp.SKImage.FromBitmap(scene);
                    using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    capture = encoded.ToArray();
                    break;
                }
            }
            await Task.Delay(25, cancellation.Token);
        }
        Assert.That(capture, Is.Not.Null, "No decoded video frame entered the scene pipeline.");
        var output = Environment.GetEnvironmentVariable("GALNET_VIDEO_CAPTURE");
        if (!string.IsNullOrWhiteSpace(output)) await File.WriteAllBytesAsync(output, capture!, cancellation.Token);
        presenter.AdvanceVideo(request.PlaybackId);
        if (!naturalEnd)
        {
            presenter.AdvanceVideo(request.PlaybackId);
            presenter.AdvanceVideo(request.PlaybackId);
        }
        await playing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(state.Layers, Is.Empty);
    }

    [TestCase(false), TestCase(true), Explicit("Requires the video assets and native LibVLC."), NonParallelizable]
    public async Task Hiding_or_replacing_during_asset_loading_prevents_late_video_publication(bool replace)
    {
        LibVLCSharp.Shared.Core.Initialize(Environment.GetEnvironmentVariable("GALNET_VIDEO_LIBVLC")!);
        using var realAssets = new AssetManager([new LocalFileProvider(Environment.GetEnvironmentVariable("GALNET_VIDEO_ASSETS")!)]);
        var assets = new DelayedAssets(realAssets);
        var state = new GamePageViewModel(new NoOpGameNavigationService());
        await using var presenter = new AvaloniaVideoPresenter(assets, state, new ImmediateDispatcher());
        var request = new VideoRenderRequest("pending-video", new LayerRenderRequest("video", "a1000000000000000000000000000011",
            new LayerTransform(), 0, LayerDisplayMode.Uniform, 1), []);
        var playing = presenter.PlayVideoAsync(request, CancellationToken.None);
        await assets.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (replace) state.SetLayer("video", new SceneLayerItem { HandleId = "video", Color = "#FF0000" });
        else state.HideLayer("video");
        await playing.WaitAsync(TimeSpan.FromSeconds(5));
        assets.Release.TrySetResult();
        if (replace)
        {
            Assert.That(state.Layers.Single().Color, Is.EqualTo("#FF0000"));
            Assert.That(state.Layers.Single().FrameSource, Is.Null);
        }
        else Assert.That(state.Layers, Is.Empty);
    }

    private sealed class DelayedAssets(IAssetManager inner) : IAssetManager
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await inner.GetFileAsync(assetId, ct);
        }
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => inner.GetFilesAsync(typeId, ct);
        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => inner.AcquireAsync<T>(assetId, ct);
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => inner.AcquireByPathAsync<T>(path, ct);
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => inner.RegisterDecoder(resourceTypeId, decoder);
        public void RegisterProvider(IAssetProvider provider) => inner.RegisterProvider(provider);
        public void Dispose() { }
    }

    private sealed class ImmediateDispatcher : IAvaloniaUiDispatcher
    {
        public int DispatchCount { get; private set; }

        public void Dispatch(Action action)
        {
            DispatchCount++;
            action();
        }

        public void Post(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
    }

    private sealed class NoOpGameNavigationService : IGameNavigationService
    {
        public PageViewModelBase? CurrentViewModel => null;
        public bool CanGoBack => false;
        public event EventHandler? CurrentViewModelChanged { add { } remove { } }
        public event EventHandler<GameNavigationChangedEventArgs>? Navigated { add { } remove { } }

        public void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade)
            where TViewModel : PageViewModelBase
        {
        }

        public Task NavigateAsync(
            PageViewModelBase viewModel,
            NavigationTransition transition = NavigationTransition.CrossFade,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task NavigateAsync<TViewModel, TArgs>(
            TArgs args,
            NavigationTransition transition = NavigationTransition.CrossFade,
            CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs> => Task.CompletedTask;

        public void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade)
            where TViewModel : PageViewModelBase
        {
        }

        public Task ResetToAsync<TViewModel>(
            NavigationTransition transition = NavigationTransition.CrossFade,
            CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase => Task.CompletedTask;

        public void GoBack(NavigationTransition transition = NavigationTransition.CrossFade)
        {
        }
    }
}
