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
        var source = JsonSerializer.Deserialize<GroupDocument>(File.ReadAllText(Path.Combine(root.FullName, "GameTestCase/Graph/groups/presentation-library.rawgalgroup")))!;
        using var registration = new GameRegistration();
        using var view = registration.AddBuiltins(dialoguePresenter: new Dialogue(), audioConfiguration: BuiltinAudioDefaults.Create(),
            characters: new CharacterCatalog([new("guide", "guide-name")])).BuildView();
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
