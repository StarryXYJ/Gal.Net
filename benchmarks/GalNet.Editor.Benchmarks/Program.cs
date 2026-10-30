using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using GalNet.Editor.Benchmarks;

if (args is ["--verify"])
{
    foreach (var count in new[] { 1000, 10000 })
    {
        using var benchmark = new ContentBenchmarks { EntryCount = count };
        benchmark.Setup();
        benchmark.Verify();
        Console.WriteLine($"Verified {count} entries, dirty reads, detached snapshots and recovery roundtrip.");
    }
    return;
}

var config = DefaultConfig.Instance.AddJob(Job.Default
    .WithRuntime(CoreRuntime.Core10_0).WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(6)
    .DontEnforcePowerPlan().WithId("EditorBaseline"));
var summaries = BenchmarkSwitcher.FromAssembly(typeof(ContentBenchmarks).Assembly).Run(args, config).ToArray();
if (summaries.Length == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors ||
        summary.Reports.Length == 0 || summary.Reports.Any(report => !report.Success)))
    Environment.ExitCode = 1;
