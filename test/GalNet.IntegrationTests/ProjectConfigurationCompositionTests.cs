using System.Text.Json;
using Avalonia.Controls;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Execution;
using GalNet.Core.Graph;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class ProjectConfigurationCompositionTests
{
    public sealed class ProjectOptions
    {
        public int AdvanceDelay { get; set; }
        public double TextSpeed { get; set; }
        public float WeatherVolume { get; set; }
    }

    [Test]
    public Task OwnDtoDrivesPlaybackTextAndCustomAudioWithoutDefaultSettings() => SampleUiTests.RunAsync(async () =>
    {
        var section = new ConfigurationSection<ProjectOptions>("my.project",
            [new("advanceDelay", new ValueDescriptor(typeof(int))), new("textSpeed", new ValueDescriptor(typeof(double))),
             new("weatherVolume", new ValueDescriptor(typeof(float)))]);
        var configuration = new ProjectConfiguration();
        configuration.Set(section, new() { AdvanceDelay = 0, TextSpeed = 0, WeatherVolume = .35f });
        configuration = JsonSerializer.Deserialize<ProjectConfiguration>(JsonSerializer.Serialize(configuration))!;
        var options = configuration.Read(section);

        var state = new GamePageViewModel(null!) { TextSpeed = options.TextSpeed };
        var page = new GamePage();
        var root = new Border { Child = page, Classes = { "galnet-player" } };
        root.Styles.Add(new ClassicTheme().CreateStyles());
        var window = new Window { Content = root, Width = 640, Height = 360 };
        window.Show();
        window.UpdateLayout();
        try
        {
            using var text = new AvaloniaDialoguePresenter(state, page, AvaloniaUiDispatcher.Instance);
            using var audio = new RecordingAudio();
            var module = new BuiltinAudioModule(audio, configuration: new AudioConfiguration(
                [new() { Id = "rain", GroupId = "ambience" }], [new() { Id = "ambience" }]));
            module.ApplyMix(new() { Groups = new Dictionary<string, AudioMixLevel> { ["ambience"] = new(options.WeatherVolume) } });
            await using var view = new GameRegistration().AddBuiltins(dialoguePresenter: text, audioModule: module).BuildView();
            var group = new Group
            {
                Id = "main",
                Entries = [
                    new PrimitiveEntry("audio.play", JsonSerializer.SerializeToElement(new { trackId = "rain", asset = "weather" })) { StableId = "weather" },
                    new PrimitiveEntry("dialogue.text", JsonSerializer.SerializeToElement(new { content = "Project text" })) { StableId = "line" }]
            };
            await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, view,
                playbackOptions: new() { AutoAdvanceIntervalMilliseconds = options.AdvanceDelay });
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            engine.StateChanged += change => { if (change.State == GameEngineState.Completed) completed.TrySetResult(); };
            await engine.AdvanceAsync();
            Assert.That(page.Dialogue.CharactersPerSecond, Is.Zero);
            Assert.That((await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2))).Gain, Is.EqualTo(.35f).Within(.00001));
            await engine.SetPlaybackModeAsync(PlaybackMode.Auto);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.That(configuration.Sections.Keys, Is.EqualTo(new[] { "my.project" }));
        }
        finally { window.Close(); }
    });

    private sealed class RecordingAudio : IAudioPlaybackHost
    {
        public AudioBackendCapabilities Capabilities { get; } = new(true, true, true);
        public ISEffectCatalog SEffects => SEffectCatalog.Empty;
        public event Action<AudioBackendEvent>? EventReceived { add { } remove { } }
        public TaskCompletionSource<AudioBackendRequest> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ApplyAsync(AudioBackendRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Action == AudioBackendAction.Start) Started.TrySetResult(request);
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }
}
