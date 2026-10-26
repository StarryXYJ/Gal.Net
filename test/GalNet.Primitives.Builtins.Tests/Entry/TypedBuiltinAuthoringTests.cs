using System.Text.Json;
using System.Threading.Channels;
using GalNet.Contracts.Builtins;
using GalNet.Core.Audio;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Entry;

public sealed class TypedBuiltinAuthoringTests
{
    [TestCase(BuiltinEntryIds.TextVoice, BuiltinEntryIds.Text)]
    [TestCase(BuiltinEntryIds.AppendVoiceNvl, BuiltinEntryIds.AppendNvl)]
    public void TypedVoiceExpansionKeepsConditionsReadingBoundaryNullAndSourceMap(string type, string textType)
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var source = new GroupDocument
        {
            Entries = [new() { Id = "voice", Type = type, Condition = "enabled",
            Parameters = new() { ["asset"] = JsonSerializer.SerializeToElement("clip"),
                ["content"] = JsonSerializer.SerializeToElement<object?>(null), ["trackId"] = JsonSerializer.SerializeToElement<object?>(null) } }]
        };
        var compiled = GalgroupCompiler.Compile(source, catalog);
        var entries = compiled.Document.Entries;
        Assert.That(entries.Select(entry => entry.TypeId), Is.EqualTo(new[] { BuiltinEntryIds.PlayAudio, textType }));
        Assert.That(entries.All(entry => entry.Condition == "enabled" && entry.BatchId is null), Is.True);
        Assert.That(entries[0].ReadingBoundaryId, Is.EqualTo("voice#2"));
        Assert.That(entries[1].ReadingBoundaryId, Is.Null);
        Assert.That(entries[0].Arguments.GetProperty("trackId").GetString(), Is.Empty);
        Assert.That(entries[0].Arguments.GetProperty("placement").GetString(), Is.EqualTo("replaceIfDifferent"));
        Assert.That(entries[1].Arguments.GetProperty("content").GetString(), Is.Empty);
        Assert.That(compiled.SourceMap["voice"], Is.EqualTo(new[] { "voice#1", "voice#2" }));
        source.Entries[0].Parameters["unexpected"] = JsonSerializer.SerializeToElement(1);
        Assert.That(() => GalgroupCompiler.Compile(source, catalog), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unexpected"));
    }

    [Test]
    public void CompositeDtosShareColorFieldsAndStillRejectInvalidTransitionInputs()
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var composites = catalog.Definitions.Where(definition => definition.Kind == EntryKind.Composite).ToArray();
        Assert.That(composites, Has.Length.EqualTo(8));
        Assert.That(composites.All(definition => definition.FieldDataType is not null), Is.True);
        Assert.That(catalog.Get(BuiltinEntryIds.BlackFadeTransition).FieldDataType, Is.EqualTo(typeof(ColorFieldOptions)));
        Assert.That(catalog.Get(BuiltinEntryIds.WhiteFadeTransition).FieldDataType, Is.EqualTo(typeof(ColorFieldOptions)));
        Assert.That(ColorFadeOptions.Schema.Select(field => field.Id), Is.EqualTo(ColorFieldOptions.Schema.Select(field => field.Id).Append("color")));
        Assert.That(() => Compile(catalog, BuiltinEntryIds.CrossFadeTransition, new { playbackHandleId = "play", oldHandleId = "old", newHandleId = "new", assetId = "asset", durationFrames = 0 }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("durationFrames"));
        Assert.That(() => Compile(catalog, BuiltinEntryIds.CrossFadeTransition, new { playbackHandleId = "play", oldHandleId = "old", newHandleId = "new", assetId = "asset", displayMode = (string?)null }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("displayMode"));
        Assert.That(() => Compile(catalog, BuiltinEntryIds.SlideTransition, new { playbackHandleId = "play", fromLayerHandleId = "old", toLayerHandleId = "new", toAssetId = "asset", toTransform = new { scaleX = 0 } }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("scale"));
        Assert.That(() => Compile(catalog, BuiltinEntryIds.CrossFadeTransition, new { playbackHandleId = "play", oldHandleId = "old", newHandleId = "new", assetId = "asset", transform = "{invalid}" }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("transform"));
    }

    [Test]
    public void ComplexDtoDefaultsChoicesAndOpenPayloadKeepTheirWireContract()
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var audio = Compile(catalog, "audio.play", new { placement = "replaceIfDifferent", autoBlocking = (string?)null }).Arguments;
        var options = GalNet.Core.Parameters.ValueCodec.DeserializeFields<AudioPlayOptions>(AudioPlayOptions.Schema, audio);
        Assert.That(options.Placement, Is.EqualTo(AudioPlacement.ReplaceIfDifferent));
        Assert.That(options.AutoBlocking, Is.Null);
        Assert.That(options.Gain, Is.EqualTo(1));
        Assert.That(() => Compile(catalog, "audio.play", new { placement = "ReplaceIfDifferent" }), Throws.TypeOf<InvalidDataException>());
        var effect = Compile(catalog, "effect.apply", new { parameters = new { projectData = new[] { 1, 2 }, nested = new { arbitrary = true } } }).Arguments;
        var effectOptions = GalNet.Core.Parameters.ValueCodec.DeserializeFields<EffectApplyOptions>(EffectApplyOptions.Schema, effect);
        Assert.That(effectOptions.Parameters.GetProperty("nested").GetProperty("arbitrary").GetBoolean(), Is.True);
        Assert.That(Compile(catalog, "layer.show", new { }).Arguments.GetProperty("transform").GetRawText(), Is.EqualTo("{}"));
        Assert.That(() => Compile(catalog, "layer.move", new { duration = -1 }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, "particle.burst", new { count = 0 }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, "video.play", new { asset = "movie", skipPoints = new[] { 2, 1 } }), Throws.TypeOf<InvalidDataException>());
        var choices = catalog.Get("animation.animate").DynamicParameters!["property"];
        Assert.That(choices.TryGetConstraint("enum", out var properties), Is.True);
        Assert.That(properties.EnumerateArray().Select(value => value.GetString()), Is.EqualTo(GalNet.Core.Scene.Layer.AnimationProperties
            .Concat(GalNet.Core.Scene.ParticleEmitterInstance.AnimationProperties).Select(property => property.Name).Append("time").Distinct(StringComparer.Ordinal)));
    }

    [TestCase("animation.stop", "playbackHandleId")]
    [TestCase("audio.seffect.stop", "instanceId")]
    [TestCase("dialogue.text", "content")]
    [TestCase("effect.stop", "instanceId")]
    [TestCase("layer.hide", "handleId")]
    [TestCase("layer.replace", "assetId")]
    [TestCase("nvl.append", "content")]
    [TestCase("particle.stop", "instanceId")]
    [TestCase("variable.set", "expression")]
    [TestCase("video.stop", "handleId")]
    public void SimpleDtoContractsPreserveOptionalTextNullAndDiagnostics(string type, string field)
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var omitted = Compile(catalog, type, new { }).Arguments;
        Assert.That(catalog.Get(type).FieldDataType, Is.Not.Null);
        Assert.That(omitted.TryGetProperty(field, out _), Is.EqualTo(type == "video.stop"));
        var explicitNull = Compile(catalog, type, new Dictionary<string, object?> { [field] = null }).Arguments;
        Assert.That(explicitNull.GetProperty(field).ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(() => Compile(catalog, type, new Dictionary<string, object?> { [field] = 42 }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(field));
        Assert.That(() => Compile(catalog, type, new { unexpected = "value" }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("unexpected"));
    }

    [Test]
    public void AnimationStopKeepsStringEnumDefaultNullAndStrictChoices()
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAnimationsModule()]);
        Assert.That(Compile(catalog, "animation.stop", new { }).Arguments.GetProperty("mode").GetString(), Is.EqualTo("AfterIteration"));
        Assert.That(Compile(catalog, "animation.stop", new { mode = (string?)null }).Arguments.GetProperty("mode").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(() => Compile(catalog, "animation.stop", new { mode = "completeImmediately" }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, "animation.stop", new { mode = 1 }), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void GalleryDtoRetainsOptionalIntegerAndRuntimePositiveIdValidation()
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateGalleryModule()]);
        Assert.That(Compile(catalog, "gallery.unlock", new { }).Arguments.TryGetProperty("id", out _), Is.False);
        Assert.That(() => Compile(catalog, "gallery.unlock", new { id = (int?)null }), Throws.TypeOf<InvalidDataException>());
        using var view = new GameRegistration().UseCatalog(catalog).AddBuiltinImplementations(catalog).BuildView();
        Assert.That(() => view.Dispatch(new PrimitiveEntry("gallery.unlock", Compile(catalog, "gallery.unlock", new { }).Arguments),
            new GameRuntime(null), default), Throws.TypeOf<InvalidDataException>().With.Message.Contains("positive integer"));
    }

    [Test]
    public void AudioStopPreservesStringBooleanDefaultsAndExplicitNull()
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAudioModule()]);
        var defaults = Compile(catalog, "audio.stop", new { }).Arguments;
        Assert.That(defaults.GetProperty("clearQueue").GetString(), Is.EqualTo("true"));
        var options = GalNet.Core.Parameters.ValueCodec.DeserializeFields<AudioStopOptions>(AudioStopOptions.Schema, defaults);
        Assert.That(options.ClearQueue, Is.True);
        var explicitNull = Compile(catalog, "audio.stop", new { clearQueue = (string?)null }).Arguments;
        Assert.That(GalNet.Core.Parameters.ValueCodec.DeserializeFields<AudioStopOptions>(AudioStopOptions.Schema, explicitNull).ClearQueue, Is.Null);
        Assert.That(() => Compile(catalog, "audio.stop", new { clearQueue = true }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, "audio.stop", new { fadeOut = -1 }), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("audio.pause")]
    [TestCase("audio.resume")]
    [TestCase("audio.next")]
    [TestCase("audio.clear")]
    public void AudioDefaultsNullAndInvalidFieldsRetainTheirContract(string type)
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAudioModule()]);
        Assert.That(catalog.Get(type).FieldDataType, Is.EqualTo(typeof(AudioTrackOptions)));
        Assert.That(Compile(catalog, type, new { }).Arguments.GetProperty("trackId").GetString(), Is.EqualTo("bgm"));
        Assert.That(Compile(catalog, type, new { trackId = (string?)null }).Arguments.GetProperty("trackId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(() => Compile(catalog, type, new { trackId = 1 }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("trackId"));
        Assert.That(() => Compile(catalog, type, new { other = "bgm" }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("other"));
    }

    [Test]
    public async Task CompiledTypedAudioControlsReachTheBackendAndCancellationStillStopsDispatch()
    {
        using var host = new RecordingHost();
        await using var view = new GameRegistration().AddBuiltins(audioHost: host).BuildView();
        var runtime = new GameRuntime(null);
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAudioModule()]);
        Dispatch("audio.play", new { asset = "first" });
        await host.TakeAsync(AudioBackendAction.Start);
        Dispatch("audio.pause", new { });
        Assert.That((await host.TakeAsync(AudioBackendAction.Pause)).TrackId, Is.EqualTo("bgm"));
        Dispatch("audio.resume", new { trackId = "bgm" });
        Assert.That((await host.TakeAsync(AudioBackendAction.Resume)).TrackId, Is.EqualTo("bgm"));
        Dispatch("audio.stop", new { });
        Assert.That((await host.TakeAsync(AudioBackendAction.Stop)).TrackId, Is.EqualTo("bgm"));
        Dispatch("audio.play", new { asset = "second" });
        await host.TakeAsync(AudioBackendAction.Start);
        Dispatch("audio.play", new { asset = "queued" });
        Dispatch("audio.clear", new { });
        Dispatch("audio.next", new { });
        Assert.That((await host.TakeAsync(AudioBackendAction.Stop)).TrackId, Is.EqualTo("bgm"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(() => view.Dispatch(new PrimitiveEntry("audio.pause", JsonSerializer.SerializeToElement(new { })),
            runtime, cancellation.Token), Throws.TypeOf<OperationCanceledException>());

        void Dispatch(string type, object arguments)
        {
            var entry = Compile(catalog, type, arguments);
            var instance = view.Dispatch(new PrimitiveEntry(entry.TypeId, entry.Arguments, "batch"), runtime, default);
            Assert.That(instance.BatchId, Is.EqualTo("batch"));
            Assert.That(instance.IsCompleted, Is.True);
        }
    }

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public void TypedWaitKeepsDefaultsRangesAndItsIndependentLifecycle(PrimitiveInvocationMode mode)
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateFlowModule()]);
        var compiled = Compile(catalog, BuiltinEntryIds.Wait, new { });
        Assert.That(compiled.Arguments.GetProperty("duration").GetSingle(), Is.EqualTo(1f));
        Assert.That(() => Compile(catalog, BuiltinEntryIds.Wait, new { duration = -1 }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("duration"));
        Assert.That(() => Compile(catalog, BuiltinEntryIds.Wait, new { duration = (float?)null }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("duration"));
        using var view = new GameRegistration().UseCatalog(catalog).AddBuiltinImplementations(catalog).BuildView();
        var instance = view.Dispatch(new PrimitiveEntry(compiled.TypeId, compiled.Arguments, "wait"), new GameRuntime(null), default, mode);
        Assert.That(instance, Is.TypeOf<WaitPrimitiveInstance>());
        Assert.That(instance.BatchId, Is.EqualTo("wait"));
        Assert.That(instance.IsCompleted, Is.EqualTo(mode != PrimitiveInvocationMode.Normal));
        instance.Skip();
        Assert.That(instance.IsCompleted, Is.True);
    }

    private static PrimitiveEntryDocument Compile(IEntryCatalog catalog, string type, object arguments) =>
        GalgroupCompiler.Compile(new GroupDocument
        {
            Entries = [new() { Id = "test", Type = type, Parameters = JsonSerializer.SerializeToElement(arguments)
                .EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()) }]
        }, catalog).Document.Entries.Single();

    private sealed class RecordingHost : IAudioPlaybackHost
    {
        private readonly Channel<AudioBackendRequest> _requests = Channel.CreateUnbounded<AudioBackendRequest>();
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects { get; } = new SEffectCatalog([]);
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            _requests.Writer.TryWrite(request);
            return Task.CompletedTask;
        }
        public async Task<AudioBackendRequest> TakeAsync(AudioBackendAction action)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var request = await _requests.Reader.ReadAsync(timeout.Token);
                if (request.Action == action) return request;
            }
        }
        public void Dispose() { }
    }
}
