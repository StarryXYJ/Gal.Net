using TitlePageViewModel = GalNet.Sample.CrossPlatform.ViewModels.TitlePageViewModel;
using Avalonia.Controls;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using CrossSample = GalNet.Sample.CrossPlatform.SampleCustomization;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleCompositionIndependenceTests
{
    [Test]
    public Task ProjectViewMappingAndLocalizationAreOwnedByEachHost() => SampleUiTests.RunAsync(async () =>
    {
        await using var first = await AvaloniaPlayerHost.CreateAsync(CrossSample.CreateHostOptions(), services =>
            CrossSample.ConfigureServices(services, views => views.Register<TitlePageViewModel, CustomTitle>()));
        await using var second = await AvaloniaPlayerHost.CreateAsync(CrossSample.CreateHostOptions(), services =>
            CrossSample.ConfigureServices(services));
        Assert.That(first.Services.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(TitlePageViewModel)), Is.EqualTo(typeof(CustomTitle)));
        Assert.That(second.Services.GetRequiredService<IPageViewRegistry>().GetViewType(typeof(TitlePageViewModel)), Is.Not.EqualTo(typeof(CustomTitle)));
        var firstText = first.Services.GetRequiredService<AvaloniaPlayerLocalization>();
        var secondText = second.Services.GetRequiredService<AvaloniaPlayerLocalization>();
        firstText.ApplyCulture("zh-CN");
        Assert.That(firstText["App.Title"], Is.EqualTo("GalNet 跨平台示例播放器"));
        Assert.That(secondText["App.Title"], Is.EqualTo("GalNet CrossPlatform Sample Player"));
        firstText.AttachGame(new GalNet.GameHost.Content.GameContent { Graph = new() });
        Assert.That(firstText["App.Title"], Is.EqualTo("GalNet 跨平台示例播放器"));
        Assert.That(firstText["Settings.Title"], Is.EqualTo("设置"));
    });

    public sealed class CustomTitle : UserControl { }
}
