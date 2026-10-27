using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Graph;
using GalNet.Core.Parameters;
using GalNet.Core.Runtime;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.Persistence;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Headless.Acceptance;

internal static class SmokeBackends
{
    public static async Task VerifyAsync(GameSnapshot snapshot)
    {
        var options = new SmokeOptions { Count = 4, Child = new() { Text = "嵌套", Next = new() { Text = "递归" } } };
        var fields = ValueCodec.SerializeFields(options);
        var decoded = ValueCodec.DeserializeFields<SmokeOptions>(SmokeOptions.Schema, fields);
        Check(decoded.Count == 4 && decoded.Child?.Next?.Text == "递归", "generated field codecs");
        Check(decoded.Enabled == true && decoded.Mode == SmokeMode.First && fields.GetProperty("enabled").ValueKind == JsonValueKind.String,
            "nullable field converters and enum choices");
        using (var structures = new ValueStructureResolver())
        {
            var resolved = structures.Resolve(SmokeOptions.Schema.Single(field => field.Id == "child"));
            Check(resolved.Kind == ValueStructureKind.Object && resolved.Members.Any(member => member.WireName == "Text"), "generated lazy nested schemas");
        }
        var section = new ConfigurationSection<SmokeSettings>("project.settings", SmokeSettings.Schema, jsonResolver: SmokeJsonContext.Default);
        var configuration = new ProjectConfiguration();
        configuration.Set(section, new() { Count = 5 });
        var configCopy = JsonCodec.Deserialize<ProjectConfiguration>(JsonCodec.Serialize(configuration))!;
        Check(configCopy.Read(section).Count == 5, "custom configuration");
        try { configCopy.Set(section, new() { Count = -1 }); throw new InvalidOperationException("Invalid configuration was accepted."); }
        catch (InvalidDataException) { }
        var defaults = new DefaultProjectOptions().ToConfiguration();
        Check(JsonCodec.Deserialize<ProjectConfiguration>(JsonCodec.Serialize(defaults))!.ReadDefaultOptions().SaveSlotCount > 0, "default configuration");

        var catalog = new ResourceTypeCatalogBuilder().Add<SmokeMeta>("project-data", SmokeJsonContext.Default, ".custom").Build();
        var codec = new AssetMetaCodec(catalog);
        var meta = (SmokeMeta)codec.Deserialize("""{"id":"custom","type":"project-data","path":"data.custom","label":"资源","future":true}""");
        Check(((SmokeMeta)codec.Deserialize(codec.Serialize(meta))).Label == "资源" && meta.ExtensionData!["future"].GetBoolean(), "custom metadata");
        meta.SetGallery([new GalleryAnnotation { Id = 1, TypeId = "images", ExtensionData = new() { ["future"] = JsonCodec.SerializeToElement(true) } }]);
        Check(meta.ReadGallery().Single().ExtensionData!["future"].GetBoolean(), "gallery annotation extension data");
        var bytes = Archive.Serialize("assets", [new GameFile(meta.Id, meta.Path, meta.TypeId, meta, [1, 2, 3])], catalog);
        var provider = new PakStreamProvider(new Dictionary<string, Func<Stream>> { ["assets"] = () => new MemoryStream(bytes) }, catalog);
        using var assets = new AssetManager([provider]);
        using (var handle = await assets.AcquireAsync<byte[]>(meta.Id)) Check(handle!.Value.SequenceEqual(new byte[] { 1, 2, 3 }), "packaged resources");
        using (var registration = new GameRegistration())
        {
            await using var view = registration.AddBuiltins().BuildView();
            var state = new GameState(view.StateCatalog);
            state.Table<Layer>().Add(new() { Id = "layer", AssetId = "sprite" });
            var restored = GameState.Restore(view.StateCatalog, state.Capture());
            Check(restored.Table<Layer>().Get("layer").AssetId == "sprite", "builtin state registration");
            var graph = GraphLoader.LoadFromJson("""{"rootNodeId":"main","nodes":[{"id":"main","type":"Group"}],"edges":[]}""");
            GalgroupLoader.LoadIntoGroupFromContent((Group)graph.Nodes.Single(),
                """{"version":3,"kind":"Compiled","entries":[{"id":"color","typeId":"layer.showColor","arguments":{"handleId":"color","color":"#FFFFFF"}}]}""");
            await using var engine = new GameEngine(graph, view);
            await engine.AdvanceAsync();
            Check(engine.State == GameEngineState.Completed && engine.Runtime.State.Table<Layer>().Get("color").Color == "#FFFFFF", "builtin generated dispatch");
        }

        var root = Path.Combine(Path.GetTempPath(), "GalNet-Aot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var saves = new FileSaveService(root);
            await saves.SaveAsync(0, new SaveRequest { Snapshot = snapshot });
            Check((await saves.LoadAsync(0))?.States.Count == snapshot.States.Count, "file save/load");
            Check(!(await saves.ListSlotsAsync())[0].IsCorrupt, "file save metadata");
            using var store = new FilePlayerVariableStore(root);
            var variables = snapshot.Variables.ToDictionary(pair => pair.Key, pair => pair.Value);
            await store.SaveAsync(variables);
            Check((await store.LoadAsync()).Count == variables.Count, "player variable store");
            await using (var progress = new FileGameProgressService(root)) { progress.MarkRead("main", "one"); await progress.FlushAsync(); }
            await using var reopened = new FileGameProgressService(root);
            Check(reopened.IsRead("main", "one"), "read progress persistence");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException($"AOT acceptance failed: {operation}.");
    }
}
