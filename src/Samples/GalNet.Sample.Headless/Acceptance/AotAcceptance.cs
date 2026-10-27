using System.Runtime.CompilerServices;
using System.Text.Json;
using GalNet.Core.Graph;
using GalNet.Core.Runtime;
using GalNet.Core.Serialization;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.Runtime;
using GalNet.Runtime.SaveLoad;

namespace GalNet.Sample.Headless.Acceptance;

internal static class AotAcceptance
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (JsonSerializer.IsReflectionEnabledByDefault) throw new InvalidOperationException("The acceptance host must disable reflection JSON.");
        if (args.Contains("--expect-aot") && RuntimeFeature.IsDynamicCodeSupported) throw new InvalidOperationException("The acceptance host is not running as NativeAOT.");

        var graph = GraphLoader.LoadFromJson("""{"name":"AOT 验收","rootNodeId":"main","nodes":[{"id":"main","type":"Group"}],"edges":[]}""");
        var group = (Group)graph.Nodes.Single();
        GalgroupLoader.LoadIntoGroupFromContent(group, """{"version":3,"kind":"Compiled","entries":[{"id":"one","typeId":"project.increment","arguments":{}}]}""");
        var stateOptions = JsonMetadata.CreateOptions(GameState.JsonOptions, SmokeJsonContext.Default);
        var states = new StateCatalogBuilder();
        states.Register<SmokeState>("project.counter", value => value.Id, jsonTypeInfo: JsonMetadata.GetTypeInfo<SmokeState[]>(stateOptions));
        var runtime = new GameRuntime(null, group.Id);
        runtime.SetVariable("save.count", 0);
        runtime.State.Configure(states.Build());
        runtime.State.Table<SmokeState>().Add(new() { Id = "counter" });
        await using var engine = new GameEngine(graph, runtime, new SmokeDispatcher(states.Build()));
        var saved = SaveManager.Serialize(engine.CreateSaveData());
        var snapshot = SaveManager.Deserialize(saved) ?? throw new InvalidDataException("Snapshot decoding failed.");
        await engine.AdvanceAsync();
        Check(engine.State == GameEngineState.Completed && runtime.GetVariable("save.count")!.AsInt() == 1, "story execution");
        Check(runtime.State.Table<SmokeState>().Get("counter").Count == 1, "custom state execution");
        await engine.RestoreFromAsync(snapshot);
        Check(runtime.State.Table<SmokeState>().Get("counter").Count == 0, "custom state restoration");
        await engine.AdvanceAsync();
        Check(runtime.GetVariable("save.count")!.AsInt() == 1, "restored execution");
        Check(runtime.EvaluateCondition("[save.count] == 1 && '文本' == '文本'"), "interpreted expressions");
        await SmokeBackends.VerifyAsync(snapshot);
        try
        {
            _ = JsonCodec.SerializeToElement(new MissingMetadata());
            throw new InvalidOperationException("Unregistered metadata unexpectedly succeeded.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("No JSON metadata", StringComparison.Ordinal)) { }
        Console.WriteLine($"AOT_ACCEPTANCE_OK dynamicCode={RuntimeFeature.IsDynamicCodeSupported} reflectionJson={JsonSerializer.IsReflectionEnabledByDefault}");
        return 0;

    }

    private static void Check(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException($"AOT acceptance failed: {operation}.");
    }

}
