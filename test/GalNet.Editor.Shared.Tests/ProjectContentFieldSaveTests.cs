using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;

namespace GeneralTest.Editor;

public sealed partial class ProjectContentSessionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SaveRejectsMismatchedVariableNamesAndRecoveryRetainsTheExactDraft(bool changeDeclaration)
    {
        var definition = new ProjectVariableDefinition
        {
            Name = "declared", DefaultValue = new() { Uid = "variable", Name = "declared", Value = VariableValue.From(1) }
        };
        _documents.CurrentDocument.PlayerVariables.Add(definition);
        await _session.SaveAsync();
        var original = File.ReadAllBytes(Path.Combine(_root, "Graph", "graph.json"));
        if (changeDeclaration) definition.Name = "renamed";
        else definition.DefaultValue.Name = "value_name";
        Assert.That(_session.IsDirty, Is.True);
        Assert.ThrowsAsync<InvalidDataException>(async () => await _session.SaveAsync());
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "Graph", "graph.json")), Is.EqualTo(original));
        Assert.That(_session.IsDirty, Is.True);
        var recovered = GalNet.Editor.Shared.Services.ProjectRecoveryStore.Read(
            Directory.GetFiles(Path.Combine(_root, ".galnet/editor/recovery"), "*.json").Single()).Graph.PlayerVariables.Single();
        Assert.That(recovered.Name, Is.EqualTo(definition.Name));
        Assert.That(recovered.DefaultValue.Name, Is.EqualTo(definition.DefaultValue.Name));
        definition.DefaultValue.Name = definition.Name;
        await _session.SaveAsync();
        Assert.That(_session.IsDirty, Is.False, "A successful save must retain exactly the captured author content.");
        var saved = _repository.Load(_root).Document.PlayerVariables.Single();
        Assert.That(saved.Name, Is.EqualTo(definition.Name));
        Assert.That(saved.DefaultValue.Name, Is.EqualTo(definition.DefaultValue.Name));
    }

    [TestCase("graph")]
    [TestCase("node")]
    [TestCase("entry")]
    [TestCase("variable")]
    [TestCase("configuration")]
    [TestCase("text")]
    public async Task PublishingOneSectionDoesNotSaveOtherFieldsOrEraseTheirDirtyState(string field)
    {
        var group = _documents.CurrentDocument.Nodes.Single(node => node.Type == "Group");
        var entry = new EditorEntryData { StableId = "entry", Type = BuiltinEntryIds.Wait,
            Parameters = new() { ["duration"] = JsonSerializer.SerializeToElement(1f) } };
        _documents.LoadedProject.GroupEntries[group.Id].Add(entry);
        var variable = new ProjectVariableDefinition { Name = "score", DefaultValue = new() { Uid = "score", Name = "score", Value = VariableValue.From(0) } };
        _documents.CurrentDocument.SaveVariables.Add(variable);
        File.WriteAllBytes(Path.Combine(_root, "notes.txt"), [1, 2]);
        var file = _session.TrackFile("notes.txt", [1, 2]);
        await _session.SaveAsync();
        var saved = _session.CaptureSaved();
        switch (field)
        {
            case "graph": _documents.CurrentDocument.Name = "unsaved"; break;
            case "node": group.StoryPackage = "unsaved"; break;
            case "entry": entry.Parameters["future"] = JsonSerializer.SerializeToElement(new { precise = 9007199254740993L }); break;
            case "variable": variable.Description = "unsaved"; break;
            case "configuration": _project.Configuration.SetRaw("unsaved", new(1, JsonSerializer.SerializeToElement(new { value = true }))); break;
            case "text": file.Data[0]++; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        var working = _session.CaptureWorking();
        Assert.That(_session.IsDirty, Is.True);
        var graphFiles = Directory.GetFiles(Path.Combine(_root, "Graph"), "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        await _session.PublishStructureAsync(new PublishContractSection());
        new PublishContractSection().Apply(saved, AuthorDocumentVersion.Saved);
        new PublishContractSection().Apply(working, AuthorDocumentVersion.Working);
        Assert.That(_session.IsDirty, Is.True);
        Assert.That(JsonSerializer.Serialize(_session.CaptureSaved()), Is.EqualTo(JsonSerializer.Serialize(saved)));
        Assert.That(JsonSerializer.Serialize(_session.CaptureWorking()), Is.EqualTo(JsonSerializer.Serialize(working)));
        foreach (var (path, bytes) in graphFiles) Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), path);
        Assert.That(File.ReadAllBytes(Path.Combine(_root, "notes.txt")), Is.EqualTo(saved.Files[file.Id].Data));
        var settings = JsonSerializer.Deserialize<GalNet.Core.Configuration.ProjectConfiguration>(File.ReadAllBytes(Path.Combine(_root, "settings.json")))!;
        Assert.That(JsonSerializer.Serialize(settings), Is.EqualTo(JsonSerializer.Serialize(saved.Configuration)));
    }

    private sealed class PublishContractSection : IProjectStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version) =>
            document.Configuration.SetRaw("published", new(1, JsonSerializer.SerializeToElement(new { value = true })));
    }
}
