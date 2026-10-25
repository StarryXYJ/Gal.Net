#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:package Microsoft.Diagnostics.Tracing.TraceEvent@3.1.21
#:property ManagePackageVersionsCentrally=false
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

if (args.Length != 1) throw new ArgumentException("Usage: dotnet run scripts/analyze-startup-trace.cs -- <startup.nettrace>");
var file = Path.GetFullPath(args[0]);
var etlx = TraceLog.CreateFromEventPipeDataFile(file, file + ".etlx", new TraceLogOptions { LocalSymbolsOnly = true });
using var log = new TraceLog(etlx);
var marks = new List<Marker>();
var pending = new Dictionary<string, JitStart>();
var jits = new List<Jit>();
var samples = new List<Sample>();
var providerCounts = new Dictionary<string, int>();
foreach (var data in log.Events)
{
    providerCounts[data.ProviderName] = providerCounts.GetValueOrDefault(data.ProviderName) + 1;
    if (data.ProviderName == "GalNet-CPU" && data.ID == (TraceEventID)1)
        marks.Add(new((string)data.PayloadValue(0), Convert.ToDouble(data.PayloadValue(2)),
            Convert.ToDouble(data.PayloadValue(3)), data.TimeStampRelativeMSec, data.ThreadID));
    if (data is MethodJittingStartedTraceData start)
        pending[$"{start.ProcessID}/{start.MethodID}"] = new(start.TimeStampRelativeMSec,
            start.MethodNamespace + "." + start.MethodName, start.ThreadID);
    if (data is MethodLoadUnloadVerboseTraceData end &&
        pending.Remove($"{end.ProcessID}/{end.MethodID}", out var first))
        jits.Add(new(first.Name, first.Time, end.TimeStampRelativeMSec, first.Thread));
    if (data.ProviderName == "Microsoft-DotNETCore-SampleProfiler")
    {
        var frames = new List<string>();
        var stack = data.CallStack();
        while (stack is not null)
        {
            frames.Add(string.IsNullOrEmpty(stack.CodeAddress.FullMethodName)
                ? stack.CodeAddress.ModuleName + "+0x" + stack.CodeAddress.Address.ToString("X")
                : stack.CodeAddress.FullMethodName);
            stack = stack.Caller;
        }
        samples.Add(new(data.TimeStampRelativeMSec, data.ThreadID, frames));
    }
}
if (log.EventsLost != 0) throw new InvalidDataException("Runtime trace lost events; do not use incomplete timings.");
if (!marks.Any(item => item.Name == "Startup.Ready")) throw new InvalidDataException("No ready mirror event");
var anchor = marks.FirstOrDefault(item => item.Name.EndsWith(".RunAvalonia", StringComparison.Ordinal))
    ?? marks.Single(item => item.Name == "Startup.Ready");
var offset = anchor.EventTime - anchor.StartUs / 1000;
var uiThread = anchor.Thread;
var regions = marks.Where(item => item.DurationUs >= 0)
    .Select(item => new Region(item.Name, offset + item.StartUs / 1000,
        offset + (item.StartUs + item.DurationUs) / 1000)).ToList();
var xaml = regions.FirstOrDefault(item => item.Name.EndsWith(".Xaml", StringComparison.Ordinal));
if (anchor.Name.EndsWith(".RunAvalonia", StringComparison.Ordinal) && xaml is not null)
    regions.Add(new("RunAvalonia_to_Xaml", anchor.EventTime, xaml.Start));
var summaries = regions.Select(region =>
{
    var intervals = jits.Where(item => item.Thread == uiThread && item.End > region.Start && item.Start < region.End)
        .Select(item => (Start: Math.Max(item.Start, region.Start), End: Math.Min(item.End, region.End)))
        .OrderBy(item => item.Start).ToArray();
    double union = 0, last = double.NegativeInfinity;
    foreach (var item in intervals) { union += Math.Max(0, item.End - Math.Max(last, item.Start)); last = Math.Max(last, item.End); }
    var selected = samples.Where(item => item.Thread == uiThread && item.Time >= region.Start && item.Time <= region.End).ToArray();
    var leaf = selected.Where(item => item.Frames.Count > 0).GroupBy(item => item.Frames[0])
        .Select(group => new { Method = group.Key, Samples = group.Count() }).OrderByDescending(item => item.Samples).Take(15).ToArray();
    var inclusive = selected.SelectMany(item => item.Frames.Distinct()).GroupBy(name => name)
        .Select(group => new { Method = group.Key, Samples = group.Count() }).OrderByDescending(item => item.Samples).Take(20).ToArray();
    return new { region.Name, WallMs = region.End - region.Start, MainThreadJitUnionMs = union,
        MainThreadJitCount = intervals.Length, SampleCount = selected.Length, TopExclusive = leaf, TopInclusive = inclusive };
}).ToArray();
var startup = regions.Single(item => item.Name == "Startup.Total");
var topJit = jits.Where(item => item.Start >= startup.Start && item.Start < startup.End)
    .OrderByDescending(item => item.End - item.Start).Take(40)
    .Select(item => new { item.Name, DurationMs = item.End - item.Start, item.Thread }).ToArray();
var report = new { DiagnosticOnly = true,
    Interpretation = "JIT event interval union is wall time, not CPU usage; samples may attribute native/JIT work to the nearest managed frame. Do not sum nested phases.",
    Trace = file, EventsLost = log.EventsLost, MainThread = uiThread, MarkerCount = marks.Count, ProviderCounts = providerCounts,
    JitCount = jits.Count, UnmatchedJitStarts = pending.Count, Regions = summaries, TopJit = topJit };
File.WriteAllText(file + ".analysis.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(summaries.Select(item => new { item.Name, item.WallMs, item.MainThreadJitUnionMs, item.MainThreadJitCount, item.SampleCount }), new JsonSerializerOptions { WriteIndented = true }));
sealed record Marker(string Name, double StartUs, double DurationUs, double EventTime, int Thread);
sealed record Region(string Name, double Start, double End);
sealed record JitStart(double Time, string Name, int Thread);
sealed record Jit(string Name, double Start, double End, int Thread);
sealed record Sample(double Time, int Thread, List<string> Frames);
