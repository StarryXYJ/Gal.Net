using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Compilation;
using GalNet.Core.I18n;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class PresentationShowcaseTests
{
    [Test]
    public async Task GameTestCase_presentation_group_compiles_executes_and_cleans_owned_state_during_catchup()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent!;
        var source = new GroupDocument();
        foreach (var chapter in new[] { "presentation-library", "audio", "shaders", "masks", "characters", "weather", "transitions", "ending" })
        {
            var group = JsonSerializer.Deserialize<GroupDocument>(File.ReadAllText(Path.Combine(root.FullName, $"GameTestCase/Graph/groups/{chapter}.rawgalgroup")))!;
            // Variant decoding is covered by the installed-game integration suite.
            source.Entries.AddRange(group.Entries.Where(entry => !entry.Type.StartsWith("variants.", StringComparison.Ordinal)));
        }
        using var registration = new GameRegistration();
        using var view = registration.AddBuiltins(dialoguePresenter: new Dialogue(), audioConfiguration: BuiltinAudioDefaults.Create(),
            characters: new CharacterCatalog([new("guide", "guide-name")])).BuildView();
        var index = 0;
        foreach (var entry in source.Entries) entry.Id = $"showcase-{index++}";
        var compiled = GalgroupCompiler.Compile(source, registration.BuildCatalog()).Document;
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var item in compiled.Entries)
        {
            var instance = view.Dispatch(new(item.TypeId, item.Arguments, item.BatchId), game, cancel.Token, PrimitiveInvocationMode.CatchUp);
            await instance.PresentationCompletion.WaitAsync(cancel.Token);
        }
        view.ValidateState(game, game.State);
        Assert.That(game.State.Table<EffectInstance>().Values, Is.Empty);
        Assert.That(game.State.Table<ParticleEmitterInstance>().Values, Is.Empty);
        Assert.That(game.State.Table<LoopAnimationState>().Values, Is.Empty);
        Assert.That(game.State.Table<AudioDuckingState>().Values, Is.Empty);
        Assert.That(game.State.Table<AudioSEffectState>().Values, Is.Empty);
        var restored = GalNet.Core.Runtime.GameState.Restore(view.StateCatalog, game.State.Capture());
        Assert.That(restored.Table<Layer>().Values.Any(layer => layer.Overlay), Is.False);
    }

    [Test]
    public void Static_shader_progress_survives_snapshot_without_a_fabricated_animation_override()
    {
        using var view = new GameRegistration().AddBuiltins().BuildView();
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        view.Dispatch(new("effect.apply", JsonSerializer.SerializeToElement(new { instanceId = "mask", program = "program", parameters = new { progress = 1 } })), game, default);
        var restored = GalNet.Core.Runtime.GameState.Restore(view.StateCatalog, game.State.Capture());
        var effect = restored.Table<EffectInstance>().Values.Single();
        Assert.That(effect.AnimationValues.ContainsKey("progress"), Is.False);
        Assert.That(JsonSerializer.Deserialize<JsonElement>(effect.Parameters).GetProperty("progress").GetInt32(), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Particle_stop_forwards_clear_and_removes_its_runtime_state(bool clear)
    {
        var presenter = new ParticleStopRecorder();
        using var view = new GameRegistration().AddBuiltins(particlePresenter: presenter).BuildView();
        var game = new GameRuntime(null, stateCatalog: view.StateCatalog);
        await view.Dispatch(new("particle.play", JsonSerializer.SerializeToElement(new { instanceId = "weather", parameters = new { particleTexture = "weather-texture" } })), game, default).PresentationCompletion;
        await view.Dispatch(new("particle.stop", JsonSerializer.SerializeToElement(new { instanceId = "weather", clear })), game, default).PresentationCompletion;
        Assert.That(presenter.Stopped, Is.EqualTo(("weather", clear)));
        Assert.That(game.State.Table<ParticleEmitterInstance>().Values, Is.Empty);
    }

    private sealed class ParticleStopRecorder : IParticlePresenter
    {
        public (string Id, bool Clear)? Stopped { get; private set; }
        public Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BurstParticlesAsync(ParticleBurstRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopParticleEmitterAsync(string instanceId, CancellationToken cancellationToken) => StopParticleEmitterAsync(instanceId, false, cancellationToken);
        public Task StopParticleEmitterAsync(string instanceId, bool clear, CancellationToken cancellationToken)
        { Stopped = (instanceId, clear); return Task.CompletedTask; }
    }

    private sealed class Dialogue : IDialoguePresenter
    {
        public void ShowDialogue() { }
        public void HideDialogue() { }
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SkipText() { }
        public Task PresentTextImmediatelyAsync(string speaker, string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public void RevealAllText() { }
    }
}
