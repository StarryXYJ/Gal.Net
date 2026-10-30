using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using GalNet.Assets.Benchmarks;

if (args is ["--verify"])
{
    foreach (var size in new[] { 16, 64 })
    {
        var benchmark = new PakBenchmarks { PayloadMiB = size };
        try
        {
            benchmark.Setup();
            benchmark.Verify();
            Console.WriteLine($"Verified {size} MiB fixture and all six benchmark operations.");
        }
        finally { benchmark.Dispose(); }
    }
    return;
}

var config = DefaultConfig.Instance.AddJob(Job.Default
    .WithRuntime(CoreRuntime.Core10_0).WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(6)
    .DontEnforcePowerPlan().WithId("PakBaseline"));
var summaries = BenchmarkSwitcher.FromAssembly(typeof(PakBenchmarks).Assembly).Run(args, config).ToArray();
if (summaries.Length == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors ||
        summary.Reports.Length == 0 || summary.Reports.Any(report => !report.Success)))
    Environment.ExitCode = 1;
