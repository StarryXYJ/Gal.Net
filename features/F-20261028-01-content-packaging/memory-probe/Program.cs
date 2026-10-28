using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Entry;
using GalNet.Core.Configuration;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

var root = Path.Combine(Path.GetTempPath(), "galnet-package-memory-" + Guid.NewGuid().ToString("N"));
var source = Path.Combine(root, "source"); var output = Path.Combine(root, "export");
Directory.CreateDirectory(Path.Combine(source, "Assets")); Directory.CreateDirectory(Path.Combine(source, "Graph", "groups"));
try
{
    var configuration = new ProjectConfiguration();
    if (args.Length > 1) configuration.Set(ContentPackagingOptions.Section, new() { MaxPakBytes = long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) * 1024 * 1024 });
    File.WriteAllText(Path.Combine(source, "settings.json"), JsonSerializer.Serialize(configuration));
    // Synthetic incompressible texture payloads exercise package I/O, not image decoding/rendering.
    var block = new byte[1024 * 1024];
    long inputBytes = 0;
    foreach (var (id, mib) in new[] { ("b0", 20), ("b1", 20), ("b2", 20), ("b3", 20), ("shared-x", 20), ("a-only", 20), ("unused", 32) })
    {
        using (var file = File.Create(Path.Combine(source, "Assets", id + ".png")))
            for (var index = 0; index < mib; index++) { RandomNumberGenerator.Fill(block); file.Write(block); }
        inputBytes += (long)mib * block.Length;
        File.WriteAllText(Path.Combine(source, "Assets", id + ".png.meta"), JsonSerializer.Serialize(new { id, type = "sprite", compress = "none" }));
    }
    File.WriteAllText(Path.Combine(source, "Graph", "graph.json"), """
        {"version":2,"name":"Memory probe","rootNodeId":"base","nodes":[
         {"id":"base","name":"Base","type":"Group","file":"groups/base.rawgalgroup"},
         {"id":"a","name":"A","type":"Group","file":"groups/a.rawgalgroup","storyPackage":"A"},
         {"id":"b","name":"B","type":"Group","file":"groups/b.rawgalgroup","storyPackage":"B"}],
         "edges":[{"id":"e1","fromNodeId":"base","fromOutlet":0,"toNodeId":"a"},
         {"id":"e2","fromNodeId":"a","fromOutlet":0,"toNodeId":"b"}],"playerVariables":[],"saveVariables":[]}
        """);
    Group("base", ["b0", "b1", "b2", "b3"]); Group("a", ["b0", "shared-x", "a-only"]); Group("b", ["b0", "shared-x"]);
    var types = BuiltinResourceTypes.CreateCatalog(); var gallery = BuiltinGalleryTypes.CreateCatalog(types);
    var watch = Stopwatch.StartNew();
    var result = await GamePackageExporter.ExportDirectoryAsync("probe", "Memory probe", source, output, types, gallery,
        new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
    if (!result.Success) throw new InvalidDataException(result.Error);
    _ = await new InstalledGameContentProvider(output, types, gallery).LoadAsync();
    var paks = Directory.GetFiles(output, "*.pak", SearchOption.AllDirectories);
    var sharedPackages = new HashSet<string>(); var unusedExcluded = true;
    foreach (var path in paks)
    {
        using var archive = Archive.Deserialize("assets", File.ReadAllBytes(path), types);
        unusedExcluded &= !archive.Contains("unused");
        if (archive.Descriptor.Kind == "story" && archive.Contains("shared-x")) sharedPackages.Add(archive.Descriptor.PackageId);
    }
    if (!unusedExcluded || !sharedPackages.SetEquals(["A", "B"])) throw new InvalidDataException("Large resource package membership is incorrect.");
    watch.Stop(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    using var process = Process.GetCurrentProcess(); process.Refresh();
    var report = JsonSerializer.Serialize(new
    {
        inputBytes, targetPakBytes = configuration.Read(ContentPackagingOptions.Section).MaxPakBytes,
        pakBytes = paks.Sum(path => new FileInfo(path).Length), largestPakBytes = paks.Max(path => new FileInfo(path).Length),
        baseParts = Directory.GetFiles(Path.Combine(output, "Assets", "Paks"), "*.pak").Length,
        storyParts = paks.Count(path => path.Contains("StoryPatches", StringComparison.Ordinal)), unusedExcluded,
        sharedDuplicatedInBothStories = sharedPackages.Count == 2,
        peakWorkingSetBytes = process.PeakWorkingSet64, afterGcManagedBytes = GC.GetTotalMemory(false), elapsedMilliseconds = watch.ElapsedMilliseconds
    }, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(report);
    if (args.Length > 0) File.WriteAllText(Path.GetFullPath(args[0]), report + Environment.NewLine);
    void Group(string id, string[] images) => File.WriteAllText(Path.Combine(source, "Graph", "groups", id + ".rawgalgroup"), JsonSerializer.Serialize(new
    {
        version = 2, kind = "Raw", entries = images.Select((image, index) => new { id = "e" + index, type = "layer.show", parameters = new { assetId = image } })
    }));
}
finally { Directory.Delete(root, true); }
