#if BENCHMARK
using System.Text.Json;
using System.Diagnostics.Tracing;
using System.Collections.Concurrent;
using GalNet.Diagnostics;

namespace GalNet.Diagnostics.Tests;

public sealed class CpuProfilerTests
{
    [Test]
    public void EventPipeMirrorPreservesMarkersAndCompletedDurationsOnlyWhenOptedIn()
    {
        using var listener = new CpuListener();
        var disabled = new CpuProfiler();
        disabled.Mark("not-mirrored");
        var enabled = new CpuProfiler(emitEventPipe: true);
        enabled.Mark("marker");
        using (enabled.Measure("scope")) { }
        enabled.Stop();
        enabled.Mark("after-stop");
        var events = listener.Events.ToArray();
        Assert.That(events.Select(item => item.Name), Is.EqualTo(new[] { "marker", "scope" }));
        Assert.That(events[0].Duration, Is.EqualTo(-1));
        Assert.That(events[1].Duration, Is.GreaterThanOrEqualTo(0));
    }

    private sealed class CpuListener : EventListener
    {
        public ConcurrentQueue<(string Name, double Duration)> Events { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "GalNet-CPU") EnableEvents(eventSource, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventId == 1)
                Events.Enqueue(((string)eventData.Payload![0]!, (double)eventData.Payload[3]!));
        }
    }

    [Test]
    public void NestedScopesAndMarksExportValidTraceAndDisposeOnlyOnce()
    {
        var profiler = new CpuProfiler();
        using (profiler.Measure("outer"))
        {
            var inner = profiler.Measure("inner");
            profiler.Mark("checkpoint");
            inner.Dispose();
            inner.Dispose();
        }
        using var stream = new MemoryStream();
        profiler.WriteTrace(stream);
        using var document = JsonDocument.Parse(stream.ToArray());
        var events = document.RootElement.GetProperty("traceEvents");
        Assert.That(events.GetArrayLength(), Is.EqualTo(3));
        var byName = events.EnumerateArray().ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.That(byName["outer"].GetProperty("dur").GetDouble(), Is.GreaterThanOrEqualTo(0));
        Assert.That(byName["inner"].GetProperty("dur").GetDouble(), Is.GreaterThanOrEqualTo(0));
        Assert.That(byName["checkpoint"].GetProperty("ph").GetString(), Is.EqualTo("i"));
    }

    [Test]
    public async Task ScopeCanCrossAwaitAndBeDisposedConcurrently()
    {
        var profiler = new CpuProfiler();
        var scope = profiler.Measure("async");
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(scope.Dispose)));
        Assert.That(profiler.Snapshot(), Has.Length.EqualTo(1));
    }

    [Test]
    public void CapacityIsBoundedAndStopIgnoresOutstandingScopes()
    {
        var profiler = new CpuProfiler(1);
        profiler.Mark("first");
        profiler.Mark("overflow");
        var outstanding = profiler.Measure("late");
        profiler.Stop();
        outstanding.Dispose();
        using var stream = new MemoryStream();
        profiler.WriteTrace(stream);
        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.That(profiler.Snapshot(), Has.Length.EqualTo(1));
        Assert.That(document.RootElement.GetProperty("droppedEvents").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void StartupCaptureExportsReadyAndCompletesOnlyOnce()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".json");
        var previousOutput = Environment.GetEnvironmentVariable("GALNET_BENCHMARK_OUTPUT");
        var previousExit = Environment.GetEnvironmentVariable("GALNET_BENCHMARK_EXIT");
        try
        {
            Environment.SetEnvironmentVariable("GALNET_BENCHMARK_OUTPUT", path);
            Environment.SetEnvironmentVariable("GALNET_BENCHMARK_EXIT", "1");
            StartupBenchmark.Start();
            Assert.That(StartupBenchmark.Complete(), Is.True);
            Assert.That(StartupBenchmark.Complete(), Is.False);
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var names = document.RootElement.GetProperty("traceEvents").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()).ToArray();
            Assert.That(names, Does.Contain("Startup.Ready"));
            Assert.That(names.Count(name => name == "Startup.Total"), Is.EqualTo(1));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GALNET_BENCHMARK_OUTPUT", previousOutput);
            Environment.SetEnvironmentVariable("GALNET_BENCHMARK_EXIT", previousExit);
            File.Delete(path);
        }
    }
}
#else
namespace GalNet.Diagnostics.Tests;

public sealed class DisabledBuildTests
{
    [Test]
    public void ProfilerImplementationIsAbsent()
    {
        var assembly = System.Reflection.Assembly.Load("GalNet.Diagnostics");
        Assert.That(assembly.GetType("GalNet.Diagnostics.CpuProfiler"), Is.Null);
        Assert.That(assembly.GetType("GalNet.Diagnostics.StartupBenchmark"), Is.Null);
        Assert.That(assembly.GetType("GalNet.Diagnostics.CpuTraceEvents"), Is.Null);
    }
}
#endif
