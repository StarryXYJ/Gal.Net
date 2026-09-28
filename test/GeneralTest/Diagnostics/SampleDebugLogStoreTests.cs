using GalNet.Sample.Avalonia.Debug;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GeneralTest.Diagnostics;

public sealed class SampleDebugLogStoreTests
{
    [Test]
    public void AddSampleDebug_enables_and_registers_the_supplied_store()
    {
        var services = new ServiceCollection();
        var store = new SampleDebugLogStore();

        services.AddSampleDebug(store);

        using var provider = services.BuildServiceProvider();
        Assert.Multiple(() =>
        {
            Assert.That(store.IsEnabled, Is.True);
            Assert.That(provider.GetRequiredService<SampleDebugLogStore>(), Is.SameAs(store));
        });
    }

    [Test]
    public void Store_retains_the_most_recent_500_lines_and_can_be_cleared()
    {
        var store = new SampleDebugLogStore();
        store.Enable();
        using var logger = new LoggerConfiguration().WriteTo.Sink(store).CreateLogger();

        for (var index = 0; index < 501; index++)
            logger.Information("Line {Index}", index);

        var lines = store.GetSnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(500));
            Assert.That(lines[0].Message, Is.EqualTo("Line 1"));
            Assert.That(lines[^1].Message, Is.EqualTo("Line 500"));
        });

        store.Clear();

        Assert.That(store.GetSnapshot(), Is.Empty);
    }
}
