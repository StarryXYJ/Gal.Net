using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Sample.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using CrossSample = GalNet.Sample.CrossPlatform.SampleCustomization;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleCompositionIndependenceTests
{
    [Test]
    public Task EachSampleOwnsItsViewMappingsTextAndTheme() => SampleUiTests.RunAsync(() =>
    {
        var desktop = new ServiceCollection();
        SampleCustomization.ConfigureServices(desktop, enableDebug: false,
            configureViews: views => views.Register<TitlePageViewModel, DesktopTitle>());
        var cross = new ServiceCollection();
        CrossSample.ConfigureServices(cross, views => views.Register<TitlePageViewModel, CrossTitle>());
        using var desktopRoot = desktop.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var crossRoot = cross.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var desktopScope = desktopRoot.CreateScope();
        using var crossScope = crossRoot.CreateScope();
        Assert.Multiple(() =>
        {
            Assert.That(desktopScope.ServiceProvider.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(TitlePageViewModel)), Is.EqualTo(typeof(DesktopTitle)));
            Assert.That(crossScope.ServiceProvider.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(TitlePageViewModel)), Is.EqualTo(typeof(CrossTitle)));
            Assert.That(SampleCustomization.PlayerTheme, Is.Not.SameAs(CrossSample.PlayerTheme));
            Assert.That(SampleCustomization.CreateHostOptions().GameTitle, Is.EqualTo("GalNet Avalonia Sample"));
            Assert.That(CrossSample.CreateHostOptions().GameTitle, Is.EqualTo("GalNet CrossPlatform Sample"));
        });
        var desktopText = desktopScope.ServiceProvider.GetRequiredService<AvaloniaPlayerLocalization>();
        var crossText = crossScope.ServiceProvider.GetRequiredService<AvaloniaPlayerLocalization>();
        Assert.That(desktopText["App.Title"], Is.EqualTo("GalNet Avalonia Sample Player"));
        Assert.That(crossText["App.Title"], Is.EqualTo("GalNet CrossPlatform Sample Player"));
        Assert.That(desktopText["Onboarding.Title"], Is.EqualTo("Choose your language"));
        Assert.That(crossText["Onboarding.Title"], Is.EqualTo("Choose your language"));
        crossText.ApplyCulture("zh-CN");
        Assert.That(crossText["App.Title"], Is.EqualTo("GalNet 跨平台示例播放器"));
        Assert.That(desktopText.CultureService.CurrentCulture.Name, Is.EqualTo("en-US"));
        crossText.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
        Assert.That(crossText["App.Title"], Is.EqualTo("GalNet 跨平台示例播放器"));
        Assert.That(crossText["Settings.Title"], Is.EqualTo("设置"));
        Assert.That(CrossSample.GetProfileDirectory("content", Path.GetTempPath()),
            Is.EqualTo(SampleCustomization.GetProfileDirectory("content", Path.GetTempPath())));
        return Task.CompletedTask;
    });

    public sealed class DesktopTitle : UserControl { }
    public sealed class CrossTitle : UserControl { }
}
