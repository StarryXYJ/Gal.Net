#if BENCHMARK
namespace GalNet.Diagnostics;

/// <summary>Environment-driven startup capture shared by desktop hosts.</summary>
public static class StartupBenchmark
{
    private static IDisposable? _startup;
    private static string? _output;
    private static int _completed;
    public static bool Failed { get; private set; }

    public static void Start()
    {
        _output = Environment.GetEnvironmentVariable("GALNET_BENCHMARK_OUTPUT");
        if (string.IsNullOrWhiteSpace(_output)) return;
        _startup = CpuProfiler.Shared.Measure("Startup.Total", "Startup");
        CpuProfiler.Shared.Mark("Startup.Main", "Startup");
    }

    /// <returns>Whether this benchmark process should close its window.</returns>
    public static bool Complete(bool success = true)
    {
        if (_startup is null || Interlocked.Exchange(ref _completed, 1) != 0) return false;
        Failed = !success;
        CpuProfiler.Shared.Mark(success ? "Startup.Ready" : "Startup.Failed", "Startup");
        _startup.Dispose();
        CpuProfiler.Shared.Stop();
        try
        {
            var path = Path.GetFullPath(_output!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary)) CpuProfiler.Shared.WriteTrace(stream);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"Startup benchmark export failed: {failure}");
            Failed = true;
            Environment.ExitCode = 1;
        }
        if (!success) Environment.ExitCode = 1;
        return Environment.GetEnvironmentVariable("GALNET_BENCHMARK_EXIT") == "1";
    }
}
#endif
