using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using GalNet.Sample.Benchmarks;

if (args is ["--verify"])
{
    foreach (var count in new[] { 100, 1000 })
    {
        using var benchmark = new GalleryBenchmarks { ItemCount = count };
        benchmark.Setup();
        var cards = benchmark.Verify();
        Console.WriteLine($"Verified {count} gallery entries and {cards} actual card visuals.");
    }
    using (var benchmark = new ThumbnailBenchmarks())
    {
        benchmark.Setup();
        benchmark.Verify();
        Console.WriteLine("Verified full and bounded PNG decode sizes.");
    }
    using (var benchmark = new ToolbarBenchmarks())
    {
        benchmark.Setup();
        benchmark.Verify();
        Console.WriteLine("Verified toolbar breakpoint and recovery.");
    }
    return;
}

if (!args.Contains("--artifacts", StringComparer.Ordinal))
{
    Console.Error.WriteLine("Pass --artifacts output/<directory> to keep benchmark reports in the repository output directory.");
    Environment.ExitCode = 1;
    return;
}

var config = DefaultConfig.Instance.AddJob(Job.Default
    .WithRuntime(CoreRuntime.Core10_0).WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(6)
    .WithInvocationCount(1).WithUnrollFactor(1).DontEnforcePowerPlan().WithId("SampleGallery"));
var summaries = BenchmarkSwitcher.FromAssembly(typeof(GalleryBenchmarks).Assembly).Run(args, config).ToArray();
if (summaries.Length == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors ||
        summary.Reports.Length == 0 || summary.Reports.Any(report => !report.Success)))
    Environment.ExitCode = 1;
