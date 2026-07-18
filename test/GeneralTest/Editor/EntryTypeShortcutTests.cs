using Avalonia.Input;
using GalNet.Core.Entry;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;

namespace GeneralTest.Editor;

public class EntryTypeShortcutTests
{
    [Test]
    public void Defaults_Should_Only_Assign_The_Four_Documented_Gestures()
    {
        var commands = EntryRegistry.Definitions.Select(definition => new EntryTypeShortcutCommand(definition)).ToList();
        var assigned = commands.Where(command => command.DefaultGesture is not null).ToDictionary(command => command.EntryType);

        Assert.That(assigned, Has.Count.EqualTo(4));
        Assert.That(assigned[TextEntry.TypeId].DefaultGesture, Is.EqualTo(new KeyGesture(Key.T)));
        Assert.That(assigned[ShowLayerEntry.TypeId].DefaultGesture, Is.EqualTo(new KeyGesture(Key.L)));
        Assert.That(assigned[HideLayerEntry.TypeId].DefaultGesture, Is.EqualTo(new KeyGesture(Key.L, KeyModifiers.Shift)));
        Assert.That(assigned[MoveLayerEntry.TypeId].DefaultGesture, Is.EqualTo(new KeyGesture(Key.L, KeyModifiers.Control)));
        Assert.That(commands.All(command => command.Context == "EntryTypePicker"), Is.True);
    }

    [Test]
    public void Service_Should_Persist_Disable_Reset_And_Detect_Context_Conflicts()
    {
        var settings = new TestSettingsService();
        var commands = EntryRegistry.Definitions.Select(definition => new EntryTypeShortcutCommand(definition)).ToList();
        var service = new EditorShortcutService(commands, settings);
        var video = commands.Single(command => command.EntryType == PlayVideoEntry.TypeId);

        Assert.That(() => service.SetGesture(video.Id, new KeyGesture(Key.T)), Throws.InvalidOperationException);

        service.SetGesture(video.Id, new KeyGesture(Key.V));
        Assert.That(settings.Settings.GestureOverrides[video.Id], Is.EqualTo("V"));
        Assert.That(service.FindByGesture(new KeyGesture(Key.V), "EntryTypePicker"), Is.SameAs(video));

        service.SetGesture(video.Id, null);
        Assert.That(video.Gesture, Is.Null);
        Assert.That(settings.Settings.GestureOverrides[video.Id], Is.Empty);

        service.ResetGesture(video.Id);
        Assert.That(video.Gesture, Is.Null);
        Assert.That(settings.Settings.GestureOverrides, Does.Not.ContainKey(video.Id));
        Assert.That(settings.SaveCount, Is.EqualTo(3));
    }

    private sealed class TestSettingsService : IEditorSettingsService
    {
        public EditorSettings Settings { get; } = new();
        public int SaveCount { get; private set; }
        public EditorSettings GetSettings() => Settings;
        public void SaveSettings() => SaveCount++;
    }
}
