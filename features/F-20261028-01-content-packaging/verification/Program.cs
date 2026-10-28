using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

if (JsonSerializer.IsReflectionEnabledByDefault) throw new Exception("Reflection JSON was not disabled.");
var types = BuiltinResourceTypes.CreateCatalog();
var root = Path.Combine(Path.GetTempPath(), "galnet-p1-reflection-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "images"));
try
{
    File.WriteAllText(Path.Combine(root, "images/idle.png"), "sprite");
    File.WriteAllText(Path.Combine(root, "images/idle.png.meta"), """{"id":"sprite","type":"sprite"}""");
    File.WriteAllText(Path.Combine(root, "atlas.variants"), """{"folder":"images","default":"idle"}""");
    File.WriteAllText(Path.Combine(root, "atlas.variants.meta"), """{"id":"atlas","type":"image-variants"}""");
    using var provider = new LocalFileProvider(root, types);
    using var local = await provider.OpenArchiveAsync("assets");
    var closure = AssetDependencyCollector.Collect(local.AssetIds.Select(id => local.GetAsset(id)!), ["atlas"], types);
    if (!closure.Complete || closure.Files.Count != 2) throw new Exception("Incomplete dependency closure.");
    foreach (var kind in new[] { "base", "story", "override" })
    {
        var control = new PakDescriptor { Kind = kind, ProjectId = "game", BuildId = "build", PackageId = kind == "base" ? "" : "extra", Mappings = kind == "override" ? new() { ["original"] = "atlas" } : [] };
        using var restored = Archive.Deserialize("assets", PakBuilder.Build("assets", closure.Files, types, descriptor: control), types);
        if (restored.Descriptor.Kind != kind || ((IAssetDependencies)restored.GetAsset("atlas")!.Metadata).GetDependencies().Direct.Count != 1) throw new Exception("Metadata/control roundtrip failed.");
    }
    Console.WriteLine("P1_REFLECTION_OFF_OK kinds=3 dependencies=2 reflectionJson=false");
}
finally { Directory.Delete(root, true); }
