using System.Text.Json;
using GalNet.Contracts.Builtins;
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

    [Test]
    public async Task CustomAudioConfigurationSupportsDraftUndoSaveAndReopen()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var path = Path.Combine(_root, "CustomAudio");
        var project = await projects.CreateAsync(path, "CustomAudio", new(), Contracts());
        var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
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
