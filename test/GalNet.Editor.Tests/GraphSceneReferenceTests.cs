using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task GraphCopyRemapsSceneReferencesAcrossGroupsOnceAndKeepsThemThroughHistoryAndReopen()
    {
        using var services = Services();
        var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "GraphSceneCopy"), "Copy", new(), Contracts());
        var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
        var author = project.Services.GetRequiredService<GraphAuthoringService>();
        var documents = project.Services.GetRequiredService<IEditorDocumentService>();
        var history = project.Services.GetRequiredService<EditorHistories>().Content;
        var first = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var second = await workspace.AddNodeAsync(GraphNodeKind.LinearGroup, 500, 200);
        static EditorEntryData Entry(string id, string type, object parameters) => new()
        {
            StableId = id,
            Type = type,
            Parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(parameters))!
        };
        documents.Groups[first.Id].Entries.Clear();
        documents.Groups[first.Id].Entries.AddRange(
        [
            Entry("show", BuiltinEntryIds.ShowLayer, new { handleId = "original", assetId = "original" }),
            Entry("transition", BuiltinEntryIds.CrossFadeTransition, new { oldHandleId = "original", newHandleId = "replacement", playbackHandleId = "transition-play" }),
            Entry("timeline", BuiltinEntryIds.PlayTimeline, new { timeline = new { playbackHandleId = "timeline-play", tracks = new[] { new { handleId = "replacement", property = "opacity" } }, events = new[] { new { type = BuiltinEntryIds.ShowLayer, parameters = new { handleId = "event-layer" } } } } })
        ]);
        documents.Groups[second.Id].Entries.Clear();
        documents.Groups[second.Id].Entries.AddRange(
        [
            Entry("hide", BuiltinEntryIds.HideLayer, new { handleId = "replacement" }),
            Entry("stop", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "transition-play" }),
            Entry("stop-timeline", BuiltinEntryIds.StopAnimation, new { playbackHandleId = "timeline-play" }),
            Entry("effect", BuiltinEntryIds.ApplyEffect, new { instanceId = "effect", targetHandleId = "event-layer" }),
            Entry("external", BuiltinEntryIds.HideLayer, new { handleId = "external" }),
            Entry("custom", "custom.entry", new { handleId = "replacement", nested = new { number = 9007199254740993L } })
        ]);
        documents.SynchronizeGroups();
        string Parameters(string id) => JsonSerializer.Serialize(documents.Groups[id].Entries.Select(entry => entry.Parameters));
        var originalFirst = Parameters(first.Id); var originalSecond = Parameters(second.Id);
        var clip = author.CopyNodes([second.Id, first.Id]);
        history.Clear();
        var pasted = await author.PasteAsync(clip, 800, 500);
        var entries = pasted.SelectMany(id => documents.Groups[id].Entries).ToArray();
        var show = entries.Single(entry => entry.Type == BuiltinEntryIds.ShowLayer);
        var transition = entries.Single(entry => entry.Type == BuiltinEntryIds.CrossFadeTransition);
        var timeline = entries.Single(entry => entry.Type == BuiltinEntryIds.PlayTimeline).Parameters["timeline"];
        var handle = transition.Parameters["newHandleId"].GetString();
        Assert.That(show.Parameters["handleId"].GetString(), Is.Not.EqualTo("original"));
        Assert.That(transition.Parameters["oldHandleId"].GetString(), Is.EqualTo(show.Parameters["handleId"].GetString()));
        Assert.That(handle, Is.Not.EqualTo("replacement"));
        Assert.That(entries.Where(entry => entry.Type == BuiltinEntryIds.HideLayer).Select(entry => entry.Parameters["handleId"].GetString()), Is.EquivalentTo(new[] { handle, "external" }));
        Assert.That(timeline.GetProperty("tracks")[0].GetProperty("handleId").GetString(), Is.EqualTo(handle));
        Assert.That(entries.Where(entry => entry.Type == BuiltinEntryIds.StopAnimation).Select(entry => entry.Parameters["playbackHandleId"].GetString()), Is.EquivalentTo(new[] { transition.Parameters["playbackHandleId"].GetString(), timeline.GetProperty("playbackHandleId").GetString() }));
        Assert.That(transition.Parameters["playbackHandleId"].GetString(), Is.Not.EqualTo("transition-play"));
        Assert.That(timeline.GetProperty("playbackHandleId").GetString(), Is.Not.EqualTo("timeline-play"));
        Assert.That(entries.Single(entry => entry.Type == BuiltinEntryIds.ApplyEffect).Parameters["targetHandleId"].GetString(), Is.EqualTo(timeline.GetProperty("events")[0].GetProperty("parameters").GetProperty("handleId").GetString()).And.Not.EqualTo("event-layer"));
        Assert.That(entries.Single(entry => entry.Type == "custom.entry").Parameters["handleId"].GetString(), Is.EqualTo("replacement"));
        Assert.That(entries.Single(entry => entry.Type == "custom.entry").Parameters["nested"].GetProperty("number").GetInt64(), Is.EqualTo(9007199254740993L));
        Assert.That(show.Parameters["assetId"].GetString(), Is.EqualTo("original"));
        Assert.That(entries.Select(entry => entry.StableId).Intersect(documents.Groups[first.Id].Entries.Concat(documents.Groups[second.Id].Entries).Select(entry => entry.StableId)), Is.Empty);
        var saved = pasted.ToDictionary(id => id, Parameters);
        await history.UndoAsync();
        Assert.That(pasted.Any(id => documents.CurrentDocument.Nodes.Any(node => node.Id == id)), Is.False);
        Assert.That(pasted.Any(id => documents.LoadedProject.GroupEntries.ContainsKey(id)), Is.False);
        Assert.That(history.CanUndo, Is.False, "The complete graph paste is one content history step.");
        await history.RedoAsync();
        foreach (var id in pasted) Assert.That(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(Parameters(id)), JsonSerializer.Deserialize<JsonElement>(saved[id])), Is.True);
        var again = await author.PasteAsync(clip, 1000, 600);
        Assert.That(again.SelectMany(id => documents.Groups[id].Entries).Single(entry => entry.Type == BuiltinEntryIds.CrossFadeTransition).Parameters["newHandleId"].GetString(), Is.Not.EqualTo(handle));
        Assert.That(Parameters(first.Id), Is.EqualTo(originalFirst)); Assert.That(Parameters(second.Id), Is.EqualTo(originalSecond));
        await workspace.SaveAsync(); var path = project.RootPath;
        await projects.CloseAsync(); project = await projects.OpenAsync(path);
        documents = project.Services.GetRequiredService<IEditorDocumentService>();
        foreach (var id in pasted) Assert.That(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(Parameters(id)), JsonSerializer.Deserialize<JsonElement>(saved[id])), Is.True);
        await projects.CloseAsync();
    }
}
