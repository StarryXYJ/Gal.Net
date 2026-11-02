using System.Reflection;
using GalNet.Sample.CrossPlatform;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class SamplePlatformLifecycleTests
{
    [Test]
    public async Task PauseDuringPendingResumeDoesNotReactivateThePlayer()
    {
        await using var app = new App();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var gate = (SemaphoreSlim)typeof(App).GetField("_composition", fields)!.GetValue(app)!;
        await gate.WaitAsync();
        var resume = app.SetPlatformActiveAsync(true, audioAvailable: true);
        var pause = app.SetPlatformActiveAsync(false);
        gate.Release();
        await Task.WhenAll(resume, pause).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(typeof(App).GetField("_platformActive", fields)!.GetValue(app), Is.False);
    }

    [Test]
    public async Task AudioFocusChangeAfterResumeIsNotOverwritten()
    {
        await using var app = new App();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var gate = (SemaphoreSlim)typeof(App).GetField("_composition", fields)!.GetValue(app)!;
        await gate.WaitAsync();
        var resume = app.SetPlatformActiveAsync(true, audioAvailable: true);
        var focusLost = app.SetAudioAvailableAsync(false);
        gate.Release();
        await Task.WhenAll(resume, focusLost).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(typeof(App).GetField("_platformActive", fields)!.GetValue(app), Is.True);
        Assert.That(typeof(App).GetField("_audioAvailable", fields)!.GetValue(app), Is.False);
    }
}
