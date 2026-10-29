using Avalonia.Headless;
using Avalonia.Threading;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using GalNet.Media.LibVlc;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test, Explicit("Requires GALNET_VIDEO_ASSETS and GALNET_VIDEO_LIBVLC; plays real Editor audio preview.")]
    public Task EditorAudioPreviewUsesTheApplicationsConfiguredVlcFactory() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly).Dispatch(async () =>
        {
            var native = Environment.GetEnvironmentVariable("GALNET_VIDEO_LIBVLC") ?? throw new InvalidOperationException("Set GALNET_VIDEO_LIBVLC.");
            var assets = Environment.GetEnvironmentVariable("GALNET_VIDEO_ASSETS") ?? throw new InvalidOperationException("Set GALNET_VIDEO_ASSETS.");
            var factory = new LibVlcFactory(new() { NativeLibraryDirectory = native, PluginDirectory = Path.Combine(native, "plugins") });
            using var services = Services(mediaFactory: factory);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "Media"), "Media", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            try
            {
                File.Copy(Path.Combine(assets, "bgm.mp3"), Path.Combine(project.RootPath, "Assets/bgm.mp3"));
                File.Copy(Path.Combine(assets, "bgm.mp3.meta"), Path.Combine(project.RootPath, "Assets/bgm.mp3.meta"));
                var catalog = project.Services.GetRequiredService<IAssetCatalogService>();
                await catalog.RefreshAsync();
                project.Services.GetRequiredService<EditorWorkspaceViewModel>().SelectedAsset = catalog.GetEntry("bgm.mp3");
                using var inspector = project.Services.GetRequiredService<AssetInspectorControlViewModel>();
                inspector.ToggleAudioPreviewCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (inspector.AudioPreviewDuration <= 1 && inspector.AudioPreviewError.Length == 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(25);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.That(inspector.AudioPreviewError, Is.Empty);
                Assert.That(inspector.AudioPreviewDuration, Is.GreaterThan(1000));
                Assert.That(inspector.IsAudioPreviewPlaying, Is.True);
                Assert.That(factory.NativeVersion, Does.StartWith("3."));
                inspector.ToggleAudioPreviewCommand.Execute(null);
                Assert.That(inspector.IsAudioPreviewPlaying, Is.False);
            }
            finally { await projects.CloseAsync(); }
            return true;
        }, TestContext.CurrentContext.CancellationToken);
}
