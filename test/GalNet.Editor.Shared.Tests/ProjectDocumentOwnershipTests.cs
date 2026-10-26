using GalNet.Contracts.Builtins;
using System.Text.Json;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Configuration;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ProjectDocumentOwnershipTests
{
    [Test]
    public void ProjectOwnsGroupContentAndPathIdentityWithoutAnyUiObject()
    {
        var documents = new EditorDocumentService();
        var entry = new EditorEntryData { StableId = "line", Type = "custom.line" };
        var loaded = new LoadedEditorProjectDocument
        {
            Document = new() { Nodes = [new() { Id = "opening", Type = "Group", File = "groups/intro.rawgalgroup" }] },
            GroupEntries = { ["opening"] = [entry] }
        };
        documents.Load(loaded);
        var group = documents.Groups["opening"];
        Assert.That(group.DocumentId, Is.EqualTo("group/opening"));
        Assert.That(group.RelativePath, Is.EqualTo("Graph/groups/intro.rawgalgroup"));
        Assert.That(documents.FindGroupByPath("Graph\\groups\\intro.rawgalgroup"), Is.SameAs(group));
        Assert.That(group.Entries, Is.SameAs(loaded.GroupEntries["opening"]));
        Assert.That(group.Entries.Single(), Is.SameAs(entry));
        Assert.That(() => documents.GetOrCreateGroup("other", group.RelativePath), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => documents.Load(loaded), Throws.InvalidOperationException);
        documents.Unload();
        Assert.That(documents.IsLoaded, Is.False);
        Assert.That(documents.Groups, Is.Empty);
        Assert.That(documents.FindGroupByPath(group.RelativePath), Is.Null);
    }

    [Test]
    public void SnapshotsOwnJsonAndAllSettingsIncludingAudioPolicies()
    {
        var project = new EditorProjectDocument
        {
            Configuration = new DefaultProjectOptions
            {
                AvailableLocales = [new("en-US")],
                AudioGroups = [new() { Id = "ambience", Name = "Environment" }],
                AudioTracks = [new AudioTrackDefinition
                {
                    Id = "custom", Name = "Custom", GroupId = "ambience", PreserveDuringCatchUp = true, DefaultGain = .4f, MaxVoices = 3, MaxQueuedItems = 4,
                    OverflowPolicy = AudioOverflowPolicy.StopOldest, QueueOverflowPolicy = AudioQueueOverflowPolicy.DropOldest
                }]
            }.ToConfiguration(),
            GroupEntries = { ["opening"] = [new() { StableId = "line", Type = "custom.line" }] }
        };
        using (var json = JsonDocument.Parse("""{"payload":{"items":[9007199254740993,null,{"enabled":true}]},"explicitNull":null}"""))
            project.GroupEntries["opening"][0].Parameters = json.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        var clone = EditorDocumentCloner.Clone(project);
        var options = project.Configuration.ReadDefaultOptions();
        options.AvailableLocales.Clear();
        options.AudioGroups[0] = options.AudioGroups[0] with { Name = "Changed group" };
        options.AudioTracks[0] = options.AudioTracks[0] with { Name = "Changed" };
        project.Configuration.Set(DefaultProjectConfiguration.Section, options);
        project.GroupEntries["opening"][0].Parameters.Clear();
        var values = clone.GroupEntries["opening"].Single().Parameters;
        Assert.That(values["payload"].GetProperty("items")[0].GetInt64(), Is.EqualTo(9007199254740993L));
        Assert.That(values["explicitNull"].ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(values.ContainsKey("omitted"), Is.False);
        Assert.That(clone.Configuration.ReadDefaultOptions().AvailableLocales.Single().Code, Is.EqualTo("en-US"));
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioTracks[0].Name, Is.EqualTo("Custom"));
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioGroups.Single().Id, Is.EqualTo("ambience"));
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioTracks[0].PreserveDuringCatchUp, Is.True);
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioTracks[0].DefaultGain, Is.EqualTo(.4f));
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioTracks[0].MaxQueuedItems, Is.EqualTo(4));
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioTracks[0].OverflowPolicy, Is.EqualTo(AudioOverflowPolicy.StopOldest));
        Assert.That(clone.Configuration.ReadDefaultOptions().AudioTracks[0].QueueOverflowPolicy, Is.EqualTo(AudioQueueOverflowPolicy.DropOldest));
    }
}
