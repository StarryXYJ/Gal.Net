using Avalonia.Headless;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.View;
using GalNet.Project.Contracts;
using GalNet.GameHost.Content;
using GalNet.Runtime.Runtime;
using GalNet.Sample.CrossPlatform;
#if GALNET_GAME_DEBUG
using GalNet.Avalonia.Debugging;
#endif
using GalNet.Sample.CrossPlatform.Presentation;
using GalNet.Avalonia.GameHost.Presentation;
using GeneralTest.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleCustomizationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task HostCompositionSharesScopedServicesAndReleasesLocalization(bool enableDebug)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            await using (var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services =>
            {
                SampleCustomization.ConfigureServices(services);
#if GALNET_GAME_DEBUG
                if (enableDebug) services.AddGameDebugTools(SampleCustomization.CreateDebugOptions(_ => Task.CompletedTask), new());
#endif
            }))
            {
                var localization = host.Services.GetRequiredService<AvaloniaPlayerLocalization>();
                Assert.That(localization.CultureService.AvailableCultures.Select(culture => culture.Name), Does.Contain("en-US"));
                Assert.That(host.Services.GetRequiredService<IPlayerSettingsService>(), Is.SameAs(host.Settings));
                Assert.That(host.Services.GetRequiredService<IGameSessionService>(), Is.SameAs(host.Session));
#if GALNET_GAME_DEBUG
                Assert.That(host.Services.GetService<GameDebugViewModel>() is not null, Is.EqualTo(enableDebug));
#endif
                Assert.That(host.View, Is.SameAs(host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.Views.PlayerView>()));
            }
            Assert.That(GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve, Is.Null);
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task ProjectCompositionExecutesCompiledScreenshotAndDoesNotShareModulesAcrossViews()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var catalog = ProjectSchemas.CreateCatalog();
            var compiled = GalgroupCompiler.Compile(new GroupDocument
            {
                Entries = [new() { Id = "capture", Type = ScreenshotSchema.TypeId }]
            }, catalog).Document.Entries.Single();
            var page = new GamePage();
            using var layers = new GalNet.Avalonia.GameHost.DefaultGameLayerFactory(new Dictionary<string, GalNet.Core.Assets.AssetHandle<GalNet.Rendering.Scene.SceneTexture>>());
            using var presenters = new AvaloniaGamePresenters(new GalNet.Avalonia.GameView.Presentation.GamePresentationState(), page, layers);
            using var firstRegistration = new GameRegistration();
            using var secondRegistration = new GameRegistration();
            var content = new GameContent { Graph = new Graph() };
            SampleCustomization.ConfigureExecution(firstRegistration, presenters, page, content, null, null, null, null);
            SampleCustomization.ConfigureExecution(secondRegistration, presenters, page, content, null, null, null, null);
            await using var first = firstRegistration.BuildView();
            await using var second = secondRegistration.BuildView();
            first.RequireExecutionCoverage();
            second.RequireInstructions([compiled.TypeId]);
            await first.DisposeAsync();
            var instance = second.Dispatch(new PrimitiveEntry(compiled.TypeId, compiled.Arguments, compiled.BatchId),
                new GameRuntime(null, "root"), default, PrimitiveInvocationMode.CatchUp);
            await instance.PresentationCompletion;
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(((ScreenshotPrimitiveInstance)instance).SavedPath, Is.Null);
            Assert.That(() => second.RequireInstructions(["other.project.entry"]),
                Throws.InvalidOperationException.With.Message.Contains("other.project.entry"));
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }
}
