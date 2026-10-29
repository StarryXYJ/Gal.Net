using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Headless;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Media.LibVlc;
using GalNet.Presentation.Abstractions.Application;
using LibVLCSharp.Shared;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class LibVlcFactoryNativeTests
{
    [Test, Explicit("Requires GALNET_VIDEO_ASSETS and GALNET_VIDEO_LIBVLC; exercises real Gallery playback and thumbnails.")]
    public Task Sessions_share_native_configuration_and_keep_independent_ownership() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly).Dispatch(async () =>
        {
            var native = Environment.GetEnvironmentVariable("GALNET_VIDEO_LIBVLC") ?? throw new InvalidOperationException("Set GALNET_VIDEO_LIBVLC.");
            var assets = Environment.GetEnvironmentVariable("GALNET_VIDEO_ASSETS") ?? throw new InvalidOperationException("Set GALNET_VIDEO_ASSETS.");
            var factory = new LibVlcFactory(new() { NativeLibraryDirectory = native, PluginDirectory = Path.Combine(native, "plugins") });
            using var runtime = new LibVlcRuntime(factory: factory);
            Assert.That(runtime.Instance, Is.Not.Null, runtime.InitializationError);
            using (var independent = factory.Create()) Assert.That(independent.Version, Is.EqualTo(runtime.Instance!.Version));
            Assert.That(factory.NativeVersion, Does.StartWith("3."));
            Assert.That(Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH"), Is.EqualTo(factory.Options.PluginDirectory));

            using var gallery = new GalleryMediaService(new Session(Path.Combine(assets, "niko.mp4")), factory);
            var item = new GalleryItemData(new GalleryItem { Id = 1, TypeId = "video", ResourceId = "video" }, true);
            using var thumbnail = await gallery.LoadVideoThumbnailAsync(item);
            Assert.That(thumbnail, Is.Not.Null, gallery.LastError);
            Assert.That(thumbnail!.PixelSize.Width, Is.GreaterThan(0));
            gallery.PlayVideo(item);
            Assert.That(gallery.LastError, Is.Null);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (gallery.VideoPlayer?.IsPlaying != true && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.That(gallery.VideoPlayer!.IsPlaying, Is.True);
            gallery.StopVideo();
            using var media = new Media(runtime.Instance!, Path.Combine(assets, "niko.mp4"), FromType.FromPath);
            await media.Parse(MediaParseOptions.ParseLocal).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(media.Duration, Is.GreaterThan(0), "Disposing the independent session must not dispose the runtime session.");

            var otherPlugins = Path.Combine(Path.GetTempPath(), "galnet-vlc-conflict-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(otherPlugins);
            try
            {
                var conflict = new LibVlcFactory(factory.Options with { PluginDirectory = otherPlugins });
                Assert.That(Assert.Throws<InvalidOperationException>(() => conflict.Create())!.Message, Does.Contain("already been initialized"));
                Assert.That(Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH"), Is.EqualTo(factory.Options.PluginDirectory));
            }
            finally { Directory.Delete(otherPlugins); }
            TestContext.Out.WriteLine($"LibVLC {factory.NativeVersion}; native={factory.Options.NativeLibraryDirectory}; plugins={factory.Options.PluginDirectory}");
            return true;
        }, TestContext.CurrentContext.CancellationToken);

    private sealed class Session(string mediaPath) : IGameSessionService, IGameGallerySession, IGalleryResourceResolver
    {
        public string GameTitle => "Native media";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => false;
        public bool CanContinue => false;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new ObservableCollection<GameSaveSlot>());
        public IGalleryDataSource? GalleryDataSource => null;
        public IGalleryResourceResolver GalleryResources => this;
        public string? ResolvePath(string resourceId) => mediaPath;
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
