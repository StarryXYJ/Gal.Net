using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task CustomAudioConfigurationSupportsDraftUndoSaveAndReopen()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var path = Path.Combine(_root, "CustomAudio");
        var project = await projects.CreateAsync(path, "CustomAudio", new(), Contracts());
        var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
        Assert.That(panel.AudioTracks, Is.Empty);
        panel.AddAudioGroupCommand.Execute(null);
        panel.AudioGroups[0].Id = "ambience";
        panel.AudioGroups[0].Name = "Environment";
        panel.AddAudioTrackCommand.Execute(null);
        panel.AudioTracks[0].Id = "rain";
        panel.AudioTracks[0].GroupId = "ambience";
        panel.AudioTracks[0].PreserveDuringCatchUp = true;
        await panel.CommitDraftAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().PreserveDuringCatchUp, Is.True);
        await panel.UndoRedoHistory.UndoAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioGroups, Is.Empty);
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks, Is.Empty);
        await panel.UndoRedoHistory.RedoAsync();
        Assert.That(panel.AudioGroups.Single().Id, Is.EqualTo("ambience"));
        panel.RemoveAudioGroupCommand.Execute(panel.AudioGroups[0]);
        await panel.CommitDraftAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().GroupId, Is.Null);
        await panel.UndoRedoHistory.UndoAsync();
        Assert.That(project.Configuration.ReadDefaultOptions().AudioTracks.Single().GroupId, Is.EqualTo("ambience"));
        await projects.SaveAsync();
        await projects.CloseAsync();
        await projects.OpenAsync(path);
        var restored = projects.Current!.Configuration.ReadDefaultOptions();
        Assert.That(restored.AudioGroups.Single().Id, Is.EqualTo("ambience"));
        Assert.That(restored.AudioTracks.Single().GroupId, Is.EqualTo("ambience"));
        Assert.That(restored.AudioTracks.Single().PreserveDuringCatchUp, Is.True);
        await projects.CloseAsync();
    }
}
