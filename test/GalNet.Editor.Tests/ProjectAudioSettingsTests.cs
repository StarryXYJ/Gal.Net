using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Core.Configuration;
using GalNet.Editor.Services;
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

    [Test]
    public async Task AudioTracksDisplayTheirIdentityAndAccessibleActions()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var audio = BuiltinAudioDefaults.Create();
            var project = await projects.CreateAsync(Path.Combine(_root, "AudioPresentation"), "AudioPresentation", new DefaultProjectOptions { AudioBuses = audio.Buses.ToList(), AudioTracks = audio.Tracks.ToList() }.ToConfiguration(), Contracts());
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var view = new ProjectSettingsPanelView { DataContext = panel }; var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show(); TimelinePump(window);
            try
            {
                var tracks = view.GetVisualDescendants().OfType<StructuredFieldEditor>().Single(editor => editor.Context.Field.Id == "audioTracks");
                tracks.IsExpanded = true; TimelinePump(window);
                Assert.That(tracks.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "#1 · bgm"), Is.True);
                var remove = tracks.GetVisualDescendants().OfType<Button>().First(button => button.Name == "remove");
                Assert.That(Avalonia.Automation.AutomationProperties.GetName(remove), Is.Not.Null.And.Not.Empty);
                Assert.That(ToolTip.GetTip(remove), Is.Not.Null);
                var firstTrack = tracks.GetVisualDescendants().OfType<StructuredFieldEditor>().First(editor => !ReferenceEquals(editor, tracks));
                firstTrack.IsExpanded = true; TimelinePump(window);
                var bus = firstTrack.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "AudioBusInput");
                var buses = audio.Buses.ToList(); buses.Add(new("ambience", "Environment"));
                await Stage(panel, "audioBuses", buses); TimelinePump(window);
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
