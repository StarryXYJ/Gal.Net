using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using Microsoft.Extensions.DependencyInjection;
#if GALNET_GAME_DEBUG
using GalNet.Avalonia.Debugging;
using GalNet.Avalonia.Debugging.Views;
#endif

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SampleActivitySurfaceTests
{
    [Test]
    public Task ActivityRecreationKeepsTheExistingPlayerAndOptionalDebugSurface() => SampleUiTests.RunAsync(async () =>
    {
        await using var app = new GalNet.Sample.CrossPlatform.App();
        var host = await AvaloniaPlayerHost.CreateAsync(
            GalNet.Sample.CrossPlatform.SampleCustomization.CreateHostOptions(), services =>
            {
                GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(services);
#if GALNET_GAME_DEBUG
                services.AddGameDebugTools(new() { RestartAsync = _ => Task.CompletedTask }, new());
#endif
            });
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GalNet.Sample.CrossPlatform.App).GetField("_player", fields)!.SetValue(app, host);
        typeof(GalNet.Sample.CrossPlatform.App).GetField("_startup", fields)!.SetValue(app, Task.CompletedTask);
        Control expected = host.View;
#if GALNET_GAME_DEBUG
        var debug = host.Services.GetRequiredService<DebugToolsOverlay>();
        debug.GameContent = host.View;
        typeof(GalNet.Sample.CrossPlatform.App).GetField("_debugSurface", fields)!.SetValue(app, debug);
        expected = debug;
#endif
        var factory = typeof(GalNet.Sample.CrossPlatform.App).GetMethod("CreateHostView", fields)!;
        ContentControl CreateView() => (ContentControl)factory.Invoke(app, null)!;
        var window = new Window { Content = CreateView() };
        window.Show();
        try
        {
            var first = (ContentControl)window.Content!;
            Assert.That(first.Content, Is.SameAs(expected));
            var replacement = CreateView();
            window.Content = replacement;
            window.UpdateLayout();
            Assert.That(first.Content, Is.Null);
            Assert.That(replacement.Content, Is.SameAs(expected));
            Assert.That(window.GetVisualDescendants().Contains(host.View), Is.True);
#if GALNET_GAME_DEBUG
            Assert.That(debug.GameContent, Is.SameAs(host.View));
#endif
        }
        finally { window.Close(); }
    });
}
