using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.Persistence;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public class ModularSaveRestoreTests
{
    [Test]
    public async Task ActualSampleAudioAndLayersSaveToANewFileAndRestoreThroughModuleRegistration()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent;
        Assert.That(directory, Is.Not.Null);
        var source = JsonSerializer.Deserialize<GroupDocument>(await File.ReadAllTextAsync(
            Path.Combine(directory!.FullName, "GameTestCase", "Graph", "groups", "intro.rawgalgroup")))!;
        var checkpointIndex = source.Entries.FindIndex(entry => entry.Id == "audio-save-checkpoint");
        Assert.That(checkpointIndex, Is.GreaterThan(0));
        // Consume real authoring entries; omit unrelated dialogue/gallery/media UI from this persistence smoke.
        var commands = new GroupDocument
        {
            Entries = source.Entries.Take(checkpointIndex)
            .Where(entry => entry.Type.StartsWith("audio.", StringComparison.Ordinal) || entry.Type.StartsWith("layer.", StringComparison.Ordinal)
                || entry.Type.StartsWith("variants.", StringComparison.Ordinal)).ToList()
        };
        var host = new Host();
        using var assets = new AssetManager([new LocalFileProvider(Path.Combine(directory.FullName, "GameTestCase", "Assets"), BuiltinResourceTypes.CreateCatalog())]);
        assets.RegisterImageVariantsDecoder();
        var variants = await assets.LoadImageVariantsCatalogAsync();
        using var registration = new GameRegistration().AddBuiltins(audioHost: host, imageVariants: variants,
            audioConfiguration: GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create());
        var compiled = GalgroupCompiler.Compile(commands, registration.BuildCatalog()).Document;
        var group = new Group { Id = "intro" };
        GalgroupLoader.LoadIntoGroupFromContent(group, JsonSerializer.Serialize(compiled));
        group.Entries.Add(new PrimitiveEntry("flow.wait", JsonSerializer.SerializeToElement(new { duration = 3600 })));
        var graph = new Graph { RootNodeId = "intro", Nodes = [group] };
        await using var view = registration.BuildView();
        var profile = Path.Combine(Path.GetTempPath(), "galnet-state-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var saves = new FileSaveService(profile);
            await using (var engine = new GameEngine(graph, view))
            {
                await engine.AdvanceAsync();
                await saves.SaveAsync(0, new SaveRequest { Snapshot = engine.CreateSaveData() });
            }
            var snapshot = await saves.LoadAsync(0);
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot!.Version, Is.EqualTo(4));
            await using var restored = new GameEngine(graph, view);
            await restored.RestoreFromAsync(snapshot);
            await using var restoration = new StateRestoreSession(view);
            await restored.SynchronizePresentationAsync(restoration.RestoreAsync);
            Assert.That(restored.Runtime.State.Table<Layer>().Values.Select(value => value.Id), Is.EquivalentTo(new[] { "background", "teacher" }));
            Assert.That(restored.Runtime.State.Table<Layer>().Get("teacher").Variants!.VariantKey, Is.EqualTo("xy_idle"));
            Assert.That(restored.Runtime.State.Table<Layer>().Get("background").Variants!.VariantKey, Is.EqualTo("classroom_speech_teacher"));
            Assert.That(restored.Runtime.State.Table<AudioTrackState>().Get("bgm").Paused, Is.True);
            Assert.That(restored.Runtime.State.Table<AudioSEffectState>().Values.Single().SEffectId, Is.EqualTo("seffect.gain"));
            Assert.That(host.Requests.Count(value => value.Action == AudioBackendAction.Start), Is.EqualTo(1));
            view.Dispatch(new PrimitiveEntry("audio.resume", JsonSerializer.SerializeToElement(new { trackId = "bgm" })), restored.Runtime, default);
            Assert.That(host.Requests.Count(value => value.Action == AudioBackendAction.Start), Is.EqualTo(2));
            Assert.That(host.Requests.Last().SEffectInstanceId, Is.EqualTo("sample-bgm-duck"));
        }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true); }
    }
    private sealed class Host : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Builtins;
        public List<AudioBackendRequest> Requests { get; } = [];
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        { Requests.Add(request); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
