using System.Text.Json;
using Avalonia;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Core.Configuration;
using GalNet.Editor.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Models;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GalNet.Editor.Views;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static IFieldBinding Setting(ProjectSettingsPanelViewModel panel, string id, string section = "galnet.project")
    {
        var form = panel.Sections.Single(item => item.Id == section);
        return form.Binding(form.Item.Fields.Single(field => field.Id == id));
    }
    private static Task Stage(ProjectSettingsPanelViewModel panel, string id, object? value, string section = "galnet.project") =>
        Setting(panel, id, section).CommitAsync(new(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })), CancellationToken.None);

    public sealed class ExtensionSettings
    {
        public List<string> Labels { get; set; } = [];
    }

    [Test]
    public async Task ExtensionSequenceUsesStructuredEditorAndPreservesOtherFields()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var field = new FieldDescriptor("labels", new ValueDescriptor(typeof(List<string>)));
            var section = new ConfigurationSection<ExtensionSettings>("extension", [field]);
            var configuration = new ProjectConfiguration();
            configuration.SetRaw("extension", new(1, JsonSerializer.SerializeToElement(new { labels = new[] { "a" }, unknown = 42 })));
            var registry = EditorRegistry.CreateDefault();
            using var editor = new ConfigurationSectionEditor(section, configuration, registry, new EditorLocalizationService(), () => true, () => { }, _ => true);
            var actual = editor.Item.Fields.Single();
            using var context = new FieldEditorContext(actual, editor.Binding(actual), registry);
            using var lease = registry.Resolve(actual)(context);
            Assert.That(lease.Control, Is.TypeOf<StructuredFieldEditor>());
            await editor.Binding(actual).CommitAsync(new(JsonSerializer.SerializeToElement(new[] { "b" })), CancellationToken.None);
            var candidate = configuration.Clone();
            editor.Apply(candidate);
            Assert.That(candidate.Sections["extension"].Data.GetProperty("unknown").GetInt32(), Is.EqualTo(42));
            Assert.That(candidate.Sections["extension"].Data.GetProperty("labels")[0].GetString(), Is.EqualTo("b"));
            return true;
        }, CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AudioTracksDisplayTheirIdentityAndAccessibleActions(bool dark)
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var audio = BuiltinAudioDefaults.Create();
            var project = await projects.CreateAsync(Path.Combine(_root, "AudioPresentation"), "AudioPresentation", new DefaultProjectOptions { AudioBuses = audio.Buses.ToList(), AudioTracks = audio.Tracks.ToList() }.ToConfiguration(), Contracts());
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var view = new ProjectSettingsPanelView { DataContext = panel };
            var window = new Window { Content = view, Width = 1200, Height = 940, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
            window.Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Assets/icons.axaml") });
            window.Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Themes/EditorButtons.axaml") });
            window.Show(); TimelinePump(window);
            try
            {
                var settings = panel.Sections.Single(section => section.AudioSettings is not null).AudioSettings!;
                var audioRoot = view.GetVisualDescendants().OfType<Expander>().Single(editor => editor.Name == "AudioSettings");
                var childExpansions = 0;
                audioRoot.Expanded += (_, args) => { if (!ReferenceEquals(args.Source, audioRoot)) childExpansions++; };
                var firstBus = settings.Buses[0];
                firstBus.IsExpanded = true; TimelinePump(window);
                var track = firstBus.Tracks[0];
                track.IsExpanded = true; TimelinePump(window);
                var trackView = view.GetVisualDescendants().OfType<Expander>().Single(editor => editor.Name == "AudioTrack" && ReferenceEquals(editor.DataContext, track));
                audioRoot.BringIntoView(new Rect(0, 0, audioRoot.Bounds.Width, 800)); TimelinePump(window);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } initialArtifacts)
                { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(initialArtifacts, dark ? "audio-settings-dark.png" : "audio-settings-light.png")); }
                window.Width = 620; TimelinePump(window);
                var audioView = view.GetVisualDescendants().OfType<ProjectAudioSettingsView>().Single(item => ReferenceEquals(item.DataContext, settings));
                Assert.That(audioView.Classes.Contains("narrow"), Is.True);
                Assert.That(trackView.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>().Where(grid => grid.Classes.Contains("audio-fields")).All(grid => grid.Columns == 1), Is.True);
                trackView.BringIntoView(); TimelinePump(window);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } narrowArtifacts)
                { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(narrowArtifacts, dark ? "audio-settings-dark-narrow.png" : "audio-settings-light-narrow.png")); }
                window.Width = 1200; TimelinePump(window);
                var advanced = trackView.GetVisualDescendants().OfType<Expander>().Single(editor => editor.Name == "AudioAdvanced");
                advanced.IsExpanded = true; TimelinePump(window);
                Assert.That(settings.Buses[0], Is.SameAs(firstBus), "Expanding a child must not rebuild its ancestor list.");
                Assert.That(firstBus.Tracks[0], Is.SameAs(track));
                Assert.That(childExpansions, Is.GreaterThanOrEqualTo(2), "Child expansion events bubble; the audio list must not rebuild in response.");
                Assert.That(trackView.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "bgm"), Is.True);
                var menu = view.GetVisualDescendants().OfType<Button>().First(button => button.Name == "AudioTrackMenu");
                Assert.That(Avalonia.Automation.AutomationProperties.GetName(menu), Is.Not.Null.And.Not.Empty);
                Assert.That(ToolTip.GetTip(menu), Is.Not.Null);
                trackView.BringIntoView(); TimelinePump(window);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "settings-audio-expanded.png")); }
                var bus = trackView.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AudioBusInput");
                var addBus = view.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AddAudioBus" && button.IsVisible && ReferenceEquals(button.DataContext, settings));
                addBus.BringIntoView(); TimelinePump(window); Click(window, addBus); TimelinePump(window);
                var added = settings.Buses[^1];
                var addedView = view.GetVisualDescendants().OfType<Expander>().Single(editor => editor.Name == "AudioBus" && ReferenceEquals(editor.DataContext, added));
                var fields = addedView.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Contexts).ToArray();
                Assert.That(await fields.Single(context => context.Field.Id == "id").CommitValueAsync("ambience"), Is.True);
                Assert.That(await fields.Single(context => context.Field.Id == "name").CommitValueAsync("Environment"), Is.True);
                TimelinePump(window);
                Assert.That(bus.Items.Cast<object>().Any(option => option.ToString() == "Environment (ambience)"), Is.True, "Unsaved bus drafts must be selectable.");
                bus.SelectedItem = bus.Items.Cast<object>().Single(option => option.ToString() == "Environment (ambience)"); TimelinePump(window);
                Assert.That(Setting(panel, "audioTracks").Read().Json!.Value[0].GetProperty("busId").GetString(), Is.EqualTo("ambience"));
                await panel.CommitDraftAsync();
                Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks[0].BusId, Is.EqualTo("ambience"));
            }
            finally { window.Close(); panel.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task NestedAudioDraftSurvivesViewClosureAndBlocksInvalidSave()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AudioDraft"), "AudioDraft", new(), Contracts());
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            await Stage(panel, "audioBuses", new[] { new { id = "music", name = "Music", extension = 17 } });
            await Stage(panel, "audioTracks", new[] { new { id = "main", busId = "music", extension = new { keep = true } } });
            var settings = panel.Sections.Single(section => section.AudioSettings is not null).AudioSettings!;
            var bus = settings.Buses.Single(); bus.IsExpanded = true;
            var track = bus.Tracks.Single(); track.IsExpanded = true;
            var view = new ProjectSettingsPanelView { DataContext = panel };
            var window = new Window { Content = view, Width = 1100, Height = 850 }; window.Show(); TimelinePump(window);
            try
            {
                var field = AudioField(view, track, "maxVoices"); field.Text = "-";
                panel.L.ApplyLocale("en-US"); TimelinePump(window);
                Assert.That(settings.Buses.Single(), Is.SameAs(bus), "Language refresh must retain nested input owners.");
                Assert.That(AudioField(view, track, "maxVoices").Text, Is.EqualTo("-"));
                window.Content = null; TimelinePump(window);
                Assert.ThrowsAsync<EditorOperationException>(async () => await panel.CommitDraftAsync());
                Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks, Is.Empty);
                window.Content = view; TimelinePump(window);
                field = AudioField(view, track, "maxVoices");
                Assert.That(field.Text, Is.EqualTo("-"), "Closing a page must retain incomplete nested input.");
                field.Text = "4"; await panel.CommitDraftAsync(); TimelinePump(window);
                Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().MaxVoices, Is.EqualTo(4));
                var data = project.Configuration.Sections["galnet.project"].Data;
                Assert.That(data.GetProperty("audioBuses")[0].GetProperty("extension").GetInt32(), Is.EqualTo(17));
                Assert.That(data.GetProperty("audioTracks")[0].GetProperty("extension").GetProperty("keep").GetBoolean(), Is.True);
                await panel.UndoRedoHistory.UndoAsync();
                Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks, Is.Empty);
                await panel.UndoRedoHistory.RedoAsync();
                Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().MaxVoices, Is.EqualTo(4));
            }
            finally { window.Close(); panel.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task NestedAudioActionsKeepReferencesAndRejectRemovedItemBindings()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AudioActions"), "AudioActions", new(), Contracts());
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            await Stage(panel, "audioBuses", new[] { new { id = "music", name = "Music" } });
            await Stage(panel, "audioTracks", new[] { new { id = "first", busId = "music", extension = 42 }, new { id = "second", busId = "music", extension = 43 } });
            var settings = panel.Sections.Single(section => section.AudioSettings is not null).AudioSettings!;
            var bus = settings.Buses.Single(); bus.IsExpanded = true;
            var first = bus.Tracks[0]; first.IsExpanded = true;
            var view = new ProjectSettingsPanelView { DataContext = panel };
            var window = new Window { Content = view, Width = 1100, Height = 850 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(await AudioField(view, bus, "id").CommitValueAsync("renamed"), Is.True); TimelinePump(window);
                Assert.That(Setting(panel, "audioTracks").Read().Json!.Value.EnumerateArray().All(value => value.GetProperty("busId").GetString() == "renamed"), Is.True);
                await bus.AddTrackCommand.ExecuteAsync(null); TimelinePump(window);
                var added = bus.Tracks[^1];
                Assert.That(Setting(panel, "audioTracks").Read().Json!.Value.EnumerateArray().Single(value => value.GetProperty("id").GetString() == added.Id).GetProperty("busId").GetString(), Is.EqualTo("renamed"));
                await added.RemoveCommand.ExecuteAsync(null); TimelinePump(window);
                Assert.That(await AudioField(view, first, "id").CommitValueAsync("second"), Is.False, "Duplicate IDs must leave the prior value intact.");
                AudioField(view, first, "id").RevertDraft();
                await first.CopyCommand.ExecuteAsync(null); TimelinePump(window);
                var copy = bus.Tracks[1];
                Assert.That(copy.Id, Is.Not.EqualTo(first.Id));
                Assert.That(Setting(panel, "audioTracks").Read().Json!.Value[1].GetProperty("extension").GetInt32(), Is.EqualTo(42));
                await copy.MoveDownCommand.ExecuteAsync(null); TimelinePump(window);
                Assert.That(bus.Tracks[^1], Is.SameAs(copy));
                var obsolete = AudioField(view, copy, "name");
                await copy.RemoveCommand.ExecuteAsync(null); TimelinePump(window);
                Assert.That(await obsolete.CommitValueAsync("removed"), Is.False);
                await bus.RemoveCommand.ExecuteAsync(null); TimelinePump(window);
                Assert.That(settings.Buses, Is.Empty);
                Assert.That(settings.UnassignedTracks.Count, Is.EqualTo(2));
                await bus.AddTrackCommand.ExecuteAsync(null);
                Assert.That(settings.UnassignedTracks.Count, Is.EqualTo(2), "A removed bus cannot add tracks through an old command.");
                Assert.That(Setting(panel, "audioTracks").Read().Json!.Value.EnumerateArray().All(value => value.GetProperty("busId").ValueKind == JsonValueKind.Null), Is.True);
                await panel.CommitDraftAsync();
                Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Count, Is.EqualTo(2));
                Assert.That(project.Configuration.ReadDefaultOptions().AudioBuses, Is.Empty);
            }
            finally { window.Close(); panel.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    private static FieldEditorContext AudioField(Control view, AudioSettingsItemViewModel item, string id) =>
        view.GetVisualDescendants().OfType<DtoEditorHost>()
            .Where(host => ReferenceEquals(host.Item, item.CommonForm) || ReferenceEquals(host.Item, item.AdvancedForm))
            .SelectMany(host => host.GetVisualDescendants().OfType<SchemaForm>().SelectMany(form => form.Contexts))
            .Single(context => context.Field.Id == id);

    [Test]
    public async Task DefaultWeatherTexturesUseStructuredMapAndProjectHistory()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "WeatherSettings"), "WeatherSettings", new(), Contracts());
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var view = new ProjectSettingsPanelView { DataContext = panel }; var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show(); TimelinePump(window);
            try
            {
                var map = view.GetVisualDescendants().OfType<StructuredFieldEditor>().Single(editor => editor.Context.Field.Id == "defaultWeatherTextures");
                Assert.That(map.Context.Field.LabelKey, Is.EqualTo("Settings.Project.DefaultWeatherTextures"));
                Assert.That(map.Context.Value.Json!.Value.ValueKind, Is.EqualTo(JsonValueKind.Object));
                await Stage(panel, "defaultWeatherTextures", new Dictionary<string, string> { ["Rain"] = "original-id" });
                map.IsExpanded = true; TimelinePump(window);
                var picker = map.GetVisualDescendants().OfType<AssetPicker>().Single();
                map.BringIntoView(); TimelinePump(window);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "settings-map-expanded.png")); }
                var row = map.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Classes.Contains("structured-map-item"));
                Assert.That(row.ColumnDefinitions.Count, Is.EqualTo(3));
                var keyInput = row.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "MapKey");
                var pickerButton = picker.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PickerButton");
                var removeButton = row.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "remove");
                var resetButton = row.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UseDefault");
                double Center(Control control) => control.TranslatePoint(new Avalonia.Point(0, control.Bounds.Height / 2), row)!.Value.Y;
                Assert.That(Center(keyInput), Is.EqualTo(Center(pickerButton)).Within(1));
                Assert.That(Center(removeButton), Is.EqualTo(Center(pickerButton)).Within(1));
                Assert.That(Center(resetButton), Is.EqualTo(Center(pickerButton)).Within(1));
                window.Width = 600; TimelinePump(window); row.BringIntoView(); TimelinePump(window);
                Assert.That(row.ColumnDefinitions.Count, Is.EqualTo(2));
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } narrowArtifacts)
                { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(narrowArtifacts, "settings-map-narrow.png")); }
                window.Width = 1000; TimelinePump(window);
                Assert.That(picker.Filter, Is.EqualTo(AssetPickerFilter.Image));
                Assert.That(picker.SelectedAssetId, Is.EqualTo("original-id"));
                Assert.That(Setting(panel, "defaultWeatherTextures").Read().Json!.Value.GetProperty("Rain").GetString(), Is.EqualTo("original-id"), "Opening must not rewrite the existing reference.");
                picker.SelectedAssetId = "rain-id"; TimelinePump(window);
                await Stage(panel, "defaultVisualPrograms", new Dictionary<string, string> { ["blur"] = "shader-id" });
                var programs = view.GetVisualDescendants().OfType<StructuredFieldEditor>().Single(editor => editor.Context.Field.Id == "defaultVisualPrograms");
                programs.IsExpanded = true; TimelinePump(window);
                var shader = programs.GetVisualDescendants().OfType<AssetPicker>().Single();
                Assert.That(shader.Filter, Is.EqualTo(AssetPickerFilter.EffectProgram));
                Assert.That(shader.SelectedAssetId, Is.EqualTo("shader-id"));
                await panel.CommitDraftAsync();
                Assert.That(project.Configuration.ReadDefaultOptions().DefaultWeatherTextures["Rain"], Is.EqualTo("rain-id"));
                Assert.That(project.Configuration.ReadDefaultOptions().DefaultVisualPrograms["blur"], Is.EqualTo("shader-id"));
                await panel.UndoRedoHistory.UndoAsync();
                Assert.That(project.Configuration.ReadDefaultOptions().DefaultWeatherTextures, Is.Empty);
            }
            finally { window.Close(); panel.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CustomAudioConfigurationSupportsDraftUndoSaveAndReopen()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var path = Path.Combine(_root, "CustomAudio");
        var project = await projects.CreateAsync(path, "CustomAudio", new(), Contracts());
        var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
        var section = panel.Sections.Single(item => item.Id == "galnet.project");
        foreach (var id in new[] { "audioBuses", "audioTracks" })
        {
            var field = section.Item.Fields.Single(item => item.Id == id);
            using var resolver = new ValueStructureResolver();
            var sequence = resolver.Resolve(field);
            Assert.That(sequence.Kind, Is.EqualTo(ValueStructureKind.Sequence));
            Assert.That(resolver.Resolve(sequence.Element!).Kind, Is.EqualTo(ValueStructureKind.Object));
        }
        await Stage(panel, "audioBuses", new[] { new { id = "ambience", name = "Environment" } });
        await Stage(panel, "audioTracks", new[] { new { id = "rain", busId = "ambience", preserveDuringCatchUp = true } });
        await panel.CommitDraftAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().PreserveDuringCatchUp, Is.True);
        await panel.UndoRedoHistory.UndoAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioBuses, Is.Empty);
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks, Is.Empty);
        await panel.UndoRedoHistory.RedoAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioBuses.Single().Id, Is.EqualTo("ambience"));
        await Stage(panel, "audioBuses", Array.Empty<object>());
        await Stage(panel, "audioTracks", new[] { new { id = "rain", preserveDuringCatchUp = true } });
        await panel.CommitDraftAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().BusId, Is.Null);
        await panel.UndoRedoHistory.UndoAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().BusId, Is.EqualTo("ambience"));
        await projects.SaveAsync();
        await projects.CloseAsync();
        await projects.OpenAsync(path);
        var restored = projects.Current!.Configuration.ReadDefaultOptions();
        Assert.That(restored.AudioBuses.Single().Id, Is.EqualTo("ambience"));
        Assert.That(restored.AudioTracks.Single().BusId, Is.EqualTo("ambience"));
        await projects.CloseAsync();
    }
}
