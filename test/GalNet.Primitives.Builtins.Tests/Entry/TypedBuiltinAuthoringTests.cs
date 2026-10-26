using System.Text.Json;
using System.Threading.Channels;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
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
    [Test]
    public async Task CompiledRepeatAndCompletionReachRuntimeAndAutoBlockingWaitsForAllIterations()
    {
        using var host = new RecordingHost();
        await using var view = new GameRegistration().AddBuiltins(audioHost: host, audioConfiguration: BuiltinAudioDefaults.Create()).BuildView();
        var runtime = new GameRuntime(null); var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAudioModule()]);
        var finite = Compile(catalog, "audio.play", new { trackId = "bgm", asset = "finite", loopCount = 2, autoBlocking = "true" });
        var instance = view.Dispatch(new PrimitiveEntry(finite.TypeId, finite.Arguments), runtime, default);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        instance.Completed += _ => completed.TrySetResult();
        var first = await host.TakeAsync(AudioBackendAction.Start); host.End(first);
        var second = await host.TakeAsync(AudioBackendAction.Start);
        Assert.That(instance.IsCompleted, Is.False); host.End(second);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var rotating = Compile(catalog, "audio.play", new { trackId = "bgm", asset = "rotating", completion = "requeueTail" });
        view.Dispatch(new PrimitiveEntry(rotating.TypeId, rotating.Arguments), runtime, default);
        var original = await host.TakeAsync(AudioBackendAction.Start); host.End(original);
        var repeated = await host.TakeAsync(AudioBackendAction.Start);
        Assert.That(repeated.AssetId, Is.EqualTo("rotating")); Assert.That(repeated.VoiceId, Is.Not.EqualTo(original.VoiceId));
        var stop = Compile(catalog, "audio.stop", new { trackId = "bgm" });
        view.Dispatch(new PrimitiveEntry(stop.TypeId, stop.Arguments), runtime, default); host.End(repeated);
        Assert.That(runtime.State.Table<AudioTrackState>().Get("bgm").Active, Is.Empty);
    }

    [TestCase("audio.play")]
    [TestCase("audio.enqueue")]
    public void AudioCompletionAndRepeatHaveExplicitDefaultsAndRejectInvalidAuthorValues(string type)
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAudioModule()]);
        var defaults = Compile(catalog, type, new { trackId = "bgm", asset = "music" }).Arguments;
        Assert.That(defaults.GetProperty("loopCount").GetInt32(), Is.EqualTo(1));
        Assert.That(defaults.GetProperty("completion").GetString(), Is.EqualTo("discard"));
        var loop = Compile(catalog, type, new { trackId = "bgm", asset = "music", loopCount = 0, completion = "requeueTail" }).Arguments;
        var options = GalNet.Core.Parameters.ValueCodec.DeserializeFields<AudioPlayOptions>(AudioPlayOptions.Schema, loop);
        Assert.That(options.LoopCount, Is.Zero); Assert.That(options.Completion, Is.EqualTo(AudioCompletion.RequeueTail));
        Assert.That(() => Compile(catalog, type, new { trackId = "bgm", loopCount = -1 }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, type, new { trackId = "bgm", loopCount = 1.5 }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, type, new { trackId = "bgm", completion = "RequeueTail" }), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(BuiltinEntryIds.TextVoice, BuiltinEntryIds.Text)]
    [TestCase(BuiltinEntryIds.AppendVoiceNvl, BuiltinEntryIds.AppendNvl)]
    public void TypedVoiceExpansionKeepsConditionsReadingBoundaryNullAndSourceMap(string type, string textType)
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var source = new GroupDocument
        {
            Entries = [new() { Id = "voice", Type = type, Condition = "enabled",
            Parameters = new() { ["asset"] = JsonSerializer.SerializeToElement("clip"),
                ["content"] = JsonSerializer.SerializeToElement<object?>(null), ["trackId"] = JsonSerializer.SerializeToElement("custom-voice") } }]
        };
        var compiled = GalgroupCompiler.Compile(source, catalog);
        var entries = compiled.Document.Entries;
        Assert.That(entries.Select(entry => entry.TypeId), Is.EqualTo(new[] { BuiltinEntryIds.PlayAudio, textType }));
        Assert.That(entries.All(entry => entry.Condition == "enabled" && entry.BatchId is null), Is.True);
        Assert.That(entries[0].ReadingBoundaryId, Is.EqualTo("voice#2"));
        Assert.That(entries[1].ReadingBoundaryId, Is.Null);
        Assert.That(entries[0].Arguments.GetProperty("trackId").GetString(), Is.EqualTo("custom-voice"));
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
        Assert.That(composites, Has.Length.EqualTo(14));
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
        var audio = Compile(catalog, "audio.play", new { trackId = "custom", placement = "replaceIfDifferent", autoBlocking = (string?)null }).Arguments;
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
        var choices = catalog.Get("animation.animate").DynamicParameters["property"];
        Assert.That(choices.TryGetConstraint("enum", out var properties), Is.True);
        Assert.That(properties.EnumerateArray().Select(value => value.GetString()), Is.EqualTo(GalNet.Contracts.Builtins.Scene.Layer.AnimationProperties
            .Concat(GalNet.Contracts.Builtins.Scene.ParticleEmitterInstance.AnimationProperties).Select(property => property.Name).Append("time").Distinct(StringComparer.Ordinal)));
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
        var required = type.StartsWith("audio.", StringComparison.Ordinal) ? new Dictionary<string, object?> { ["trackId"] = "custom" } : new Dictionary<string, object?>();
        var omitted = Compile(catalog, type, required).Arguments;
        Assert.That(catalog.Get(type).FieldDataType, Is.Not.Null);
        Assert.That(omitted.TryGetProperty(field, out _), Is.EqualTo(type == "video.stop"));
        var explicitNull = Compile(catalog, type, required.Concat(new Dictionary<string, object?> { [field] = null }).ToDictionary()).Arguments;
        Assert.That(explicitNull.GetProperty(field).ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(() => Compile(catalog, type, required.Concat(new Dictionary<string, object?> { [field] = 42 }).ToDictionary()),
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
        var defaults = Compile(catalog, "audio.stop", new { trackId = "custom" }).Arguments;
        Assert.That(defaults.GetProperty("clearQueue").GetString(), Is.EqualTo("true"));
        var options = GalNet.Core.Parameters.ValueCodec.DeserializeFields<AudioStopOptions>(AudioStopOptions.Schema, defaults);
        Assert.That(options.ClearQueue, Is.True);
        var explicitNull = Compile(catalog, "audio.stop", new { trackId = "custom", clearQueue = (string?)null }).Arguments;
        Assert.That(GalNet.Core.Parameters.ValueCodec.DeserializeFields<AudioStopOptions>(AudioStopOptions.Schema, explicitNull).ClearQueue, Is.Null);
        Assert.That(() => Compile(catalog, "audio.stop", new { clearQueue = true }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Compile(catalog, "audio.stop", new { fadeOut = -1 }), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("audio.play")]
    [TestCase("audio.enqueue")]
    [TestCase("audio.stop")]
    [TestCase("audio.seffect.apply")]
    [TestCase("audio.seffect.stop")]
    [TestCase(BuiltinEntryIds.TextVoice)]
    [TestCase(BuiltinEntryIds.AppendVoiceNvl)]
    [TestCase("audio.pause")]
    [TestCase("audio.resume")]
    [TestCase("audio.next")]
    [TestCase("audio.clear")]
    public void AudioControlsRequireExplicitNonNullTrackTargets(string type)
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var track = catalog.Get(type).DynamicParameters["trackId"];
        Assert.That(track.IsRequired, Is.True);
        Assert.That(track.DefaultValue, Is.Null);
        Assert.That(() => Compile(catalog, type, new { }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("trackId"));
        var supplied = new Dictionary<string, object?> { ["trackId"] = "custom" };
        if (type is BuiltinEntryIds.TextVoice or BuiltinEntryIds.AppendVoiceNvl) supplied["asset"] = "clip";
        Assert.That(PrimitiveArgumentHelper.Normalize(catalog.Get(type).DynamicParameters, JsonSerializer.SerializeToElement(supplied)).GetProperty("trackId").GetString(), Is.EqualTo("custom"));
        Assert.That(() => Compile(catalog, type, new { trackId = (string?)null }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("trackId"));
        Assert.That(() => Compile(catalog, type, new { trackId = 1 }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("trackId"));
        Assert.That(() => Compile(catalog, type, new { other = "bgm" }), Throws.TypeOf<InvalidDataException>().With.Message.Contains("other"));
    }

    [Test]
    public async Task AudioModuleOwnsItsTrackConfigurationWithoutRuntimeSettings()
    {
        using var host = new RecordingHost();
        var defaults = BuiltinAudioDefaults.Create();
        var tracks = defaults.Tracks.ToList();
        var configuration = new AudioConfiguration(tracks, defaults.Groups);
        await using var view = new GameRegistration().RegisterSchema(BuiltinSchemas.CreateAudioModule())
            .AddBuiltinAudio(host, configuration).BuildView();
        tracks[0] = tracks[0] with { Id = "reassigned", DefaultGain = 0 };
        tracks.Clear();

        view.Dispatch(new PrimitiveEntry("audio.play", JsonSerializer.SerializeToElement(new { trackId = "bgm", asset = "music" })),
            new GameRuntime(null), CancellationToken.None);
        var started = await host.TakeAsync(AudioBackendAction.Start);
        Assert.That(started.TrackId, Is.EqualTo("bgm"));
        Assert.That(started.Gain, Is.EqualTo(.8f));
    }

    [Test]
    public async Task CompiledTypedAudioControlsReachTheBackendAndCancellationStillStopsDispatch()
    {
        using var host = new RecordingHost();
        await using var view = new GameRegistration().AddBuiltins(audioHost: host, audioConfiguration: BuiltinAudioDefaults.Create()).BuildView();
        var runtime = new GameRuntime(null);
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateAudioModule()]);
        Dispatch("audio.play", new { trackId = "bgm", asset = "first" });
        await host.TakeAsync(AudioBackendAction.Start);
        Dispatch("audio.pause", new { trackId = "bgm" });
        Assert.That((await host.TakeAsync(AudioBackendAction.Pause)).TrackId, Is.EqualTo("bgm"));
        Dispatch("audio.resume", new { trackId = "bgm" });
        Assert.That((await host.TakeAsync(AudioBackendAction.Resume)).TrackId, Is.EqualTo("bgm"));
        Dispatch("audio.stop", new { trackId = "bgm" });
        Assert.That((await host.TakeAsync(AudioBackendAction.Stop)).TrackId, Is.EqualTo("bgm"));
        Dispatch("audio.play", new { trackId = "bgm", asset = "second" });
        await host.TakeAsync(AudioBackendAction.Start);
        Dispatch("audio.play", new { trackId = "bgm", asset = "queued" });
        Dispatch("audio.clear", new { trackId = "bgm" });
        Dispatch("audio.next", new { trackId = "bgm" });
        Assert.That((await host.TakeAsync(AudioBackendAction.Stop)).TrackId, Is.EqualTo("bgm"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(() => view.Dispatch(new PrimitiveEntry("audio.pause", JsonSerializer.SerializeToElement(new { trackId = "bgm" })),
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
        public event Action<AudioBackendEvent>? EventReceived;
        public void End(AudioBackendRequest request) => EventReceived?.Invoke(new(AudioBackendEventKind.Ended, request.TrackId, request.VoiceId, request.Generation));
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
