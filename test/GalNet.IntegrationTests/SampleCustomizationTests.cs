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
using GalNet.Sample.Avalonia;
using GalNet.Sample.Avalonia.Debug;
using GalNet.Sample.Avalonia.Presentation;
using GalNet.Sample.Avalonia.Services;
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
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var services = new ServiceCollection();
            SampleCustomization.ConfigureServices(services, enableDebug);
            await using (var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                await using var first = root.CreateAsyncScope();
                await using var second = root.CreateAsyncScope();
                var localization = first.ServiceProvider.GetRequiredService<SampleLocalizationService>();
                Assert.That(localization.CultureService.AvailableCultures.Select(culture => culture.Name), Does.Contain("en-US"));
                Assert.That(second.ServiceProvider.GetRequiredService<SampleLocalizationService>(), Is.SameAs(localization));
                var settings = first.ServiceProvider.GetRequiredService<AvaloniaPlayerSettingsService>();
                Assert.That(first.ServiceProvider.GetRequiredService<IPlayerSettingsService>(), Is.SameAs(settings));
                Assert.That(second.ServiceProvider.GetRequiredService<AvaloniaPlayerSettingsService>().Editors, Is.Not.SameAs(settings.Editors));
                var game = first.ServiceProvider.GetRequiredService<SampleGameSessionService>();
                Assert.That(first.ServiceProvider.GetRequiredService<IGameSessionService>(), Is.SameAs(game));
                Assert.That(first.ServiceProvider.GetService<SampleDebugViewModel>() is not null, Is.EqualTo(enableDebug));
            }
            Assert.That(SampleLocalizationService.Current, Is.Null);
            return true;
        }, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public async Task ProjectCompositionExecutesCompiledScreenshotAndDoesNotShareModulesAcrossViews()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenshotTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var catalog = ProjectSchemas.CreateCatalog();
            var compiled = GalgroupCompiler.Compile(new GroupDocument
            {
                Entries = [new() { Id = "capture", Type = ScreenshotSchema.TypeId }]
            }, catalog).Document.Entries.Single();
            var page = new GamePage();
            using var layers = new GalNet.Avalonia.GameHost.DefaultGameLayerFactory(new Dictionary<string, GalNet.Core.Assets.AssetHandle<GalNet.Rendering.Scene.SceneTexture>>());
            using var presenters = new AvaloniaGamePresenters(new GamePageViewModel(null!), page, layers);
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
