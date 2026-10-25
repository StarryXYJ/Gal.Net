#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Diagnostics.Tracing.TraceEvent@3.1.21
using System.Globalization;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Symbols;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--symbols"))
    throw new ArgumentException("Usage: dotnet run scripts/analyze-startup-native.cs -- <startup.etl> <pid> [--symbols]");
var file = Path.GetFullPath(args[0]);
var pid = int.Parse(args[1], CultureInfo.InvariantCulture);
var etlx = TraceLog.CreateFromEventTraceLogFile(file, file + ".etlx", new TraceLogOptions { LocalSymbolsOnly = true });
using var log = new TraceLog(etlx);
if (log.EventsLost != 0) throw new InvalidDataException($"Trace lost {log.EventsLost} events; recapture before interpreting it.");
// The iterator reuses event objects. Retained events must be cloned.
var events = log.Events.Where(item => item.ProcessID == pid).Select(item => item.Clone()).ToArray();
var marks = events.Where(item => item.ProviderGuid == Guid.Parse("b73a0b39-c81d-5026-2e0f-108dad0eff68") && (int)item.ID == 1).ToArray();
if (!marks.Any(item => (string)item.PayloadValue(0) == "Startup.Ready")) throw new InvalidDataException("No ready phase event for this PID.");
var anchor = marks.FirstOrDefault(item => ((string)item.PayloadValue(0)).EndsWith(".RunAvalonia", StringComparison.Ordinal))
    ?? marks.Single(item => (string)item.PayloadValue(0) == "Startup.Ready");
var offset = anchor.TimeStampRelativeMSec - Convert.ToDouble(anchor.PayloadValue(2), CultureInfo.InvariantCulture) / 1000;
var thread = anchor.ThreadID;
var regions = marks.Where(item => Convert.ToDouble(item.PayloadValue(3), CultureInfo.InvariantCulture) >= 0)
    .Select(item => new Region((string)item.PayloadValue(0),
        offset + Convert.ToDouble(item.PayloadValue(2), CultureInfo.InvariantCulture) / 1000,
        offset + (Convert.ToDouble(item.PayloadValue(2), CultureInfo.InvariantCulture) + Convert.ToDouble(item.PayloadValue(3), CultureInfo.InvariantCulture)) / 1000)).ToList();
var xaml = regions.FirstOrDefault(item => item.Name.EndsWith(".Xaml", StringComparison.Ordinal));
if (((string)anchor.PayloadValue(0)).EndsWith(".RunAvalonia", StringComparison.Ordinal) && xaml is not null)
    regions.Add(new("RunAvalonia_to_Xaml", anchor.TimeStampRelativeMSec, xaml.Start));
var samples = events.OfType<SampledProfileTraceData>().Where(item => item.ThreadID == thread).ToArray();
var modules = samples.SelectMany(Frames).Select(item => item.ModuleFile).Where(item => item is not null).Distinct().ToArray();
if (args.Length == 3)
{
    using var writer = File.CreateText(file + ".symbols.log");
    using var symbols = new SymbolReader(writer, "srv*" + Path.GetFullPath("Temp/native-symbols") + "*https://msdl.microsoft.com/download/symbols");
    string[] systemModules = ["dxgi", "d3d11", "ntdll", "kernelbase", "kernel32", "combase", "imagehlp", "wintrust", "crypt32"];
    foreach (var module in modules.Where(item => systemModules.Contains(item!.Name, StringComparer.OrdinalIgnoreCase)))
        log.CodeAddresses.LookupSymbolsForModule(symbols, module!);
}
var stacks = samples.Select(item => new Sample(item.TimeStampRelativeMSec,
    Frames(item).Select(address => address.ModuleName + "!" +
        (string.IsNullOrEmpty(address.FullMethodName) ? "0x" + address.Address.ToString("X", CultureInfo.InvariantCulture) : address.FullMethodName)).ToArray())).ToArray();
var summaries = regions.Select(region =>
{
    var selected = stacks.Where(item => item.Time >= region.Start && item.Time <= region.End).ToArray();
    return new { region.Name, StartMs = region.Start, EndMs = region.End, WallMs = region.End - region.Start, Samples = selected.Length,
        TopExclusive = Counts(selected.Select(item => item.Frames.FirstOrDefault() ?? "[no stack]"), 25),
        TopInclusive = Counts(selected.SelectMany(item => item.Frames.Distinct()), 80) };
}).ToArray();
var report = new { DiagnosticOnly = true,
    Interpretation = "CPU samples are counts, not exact wall time; no context-switch/wait or GPU completion analysis. Nested phases and inclusive frames cannot be summed.",
    Trace = file, ProcessId = pid, EventsLost = log.EventsLost, MainThread = thread, MarkerCount = marks.Length,
    ResolvedMicrosoftSymbols = args.Length == 3, Regions = summaries,
    Modules = modules.Select(item => new { item!.Name, item.FilePath }), Stacks = stacks };
File.WriteAllText(file + ".analysis.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(summaries.Select(item => new { item.Name, item.WallMs, item.Samples }), new JsonSerializerOptions { WriteIndented = true }));

static IEnumerable<TraceCodeAddress> Frames(TraceEvent item)
{
    for (var stack = item.CallStack(); stack is not null; stack = stack.Caller) yield return stack.CodeAddress;
}
static object[] Counts(IEnumerable<string> names, int limit) => names.GroupBy(name => name).OrderByDescending(group => group.Count())
    .Take(limit).Select(group => (object)new { Frame = group.Key, Samples = group.Count() }).ToArray();
sealed record Region(string Name, double Start, double End);
sealed record Sample(double Time, string[] Frames);
