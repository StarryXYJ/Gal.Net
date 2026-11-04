using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor.Shared;

public sealed class EntryPresetParametersTests
{
    private sealed class Catalog : IEntryCatalog
    {
        private readonly EntryDefinition _definition = new("custom", "Test", () => throw new NotSupportedException(), EntryKind.Primitive,
            new DynamicParameterTable([new(new FieldDescriptor("private", new(typeof(string))).WithPresetExclusion()), new(new FieldDescriptor("value", new(typeof(int))))]));
        public IReadOnlyList<EntryDefinition> Definitions => [_definition];
        public bool TryGet(string type, out EntryDefinition definition) { definition = _definition; return type == "custom"; }
        public EntryDefinition Get(string type) => _definition;
        public Entry Create(string type, int id = 0, string condition = "", JsonElement? arguments = null) => throw new NotSupportedException();
    }
    private static Dictionary<string, JsonElement> Parameters(string json) => JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());

    [Test]
    public void ExcludedFieldsAreOmittedAndCurrentValuesSurviveApplication()
    {
        var catalog = new Catalog();
        var snapshot = EntryPresetParameters.Capture(catalog, "custom", Parameters("""{"private":"source","value":12,"unknown":true}"""));
        Assert.That(snapshot.ContainsKey("private"), Is.False);
        var result = EntryPresetParameters.Apply(catalog, "custom", snapshot, Parameters("""{"private":"target","value":1,"old":true}"""));
        Assert.That(result["private"].GetString(), Is.EqualTo("target"));
        Assert.That(result["value"].GetInt32(), Is.EqualTo(12));
        Assert.That(result.ContainsKey("unknown"), Is.True);
        Assert.That(result.ContainsKey("old"), Is.False);
    }

    [Test]
    public void NestedTimelineOutputsAreRemappedAndTargetPlaybackIdentityIsPreserved()
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var source = Parameters("""{"timeline":{"playbackHandleId":"source-play","events":[{"frame":0,"type":"layer.show","parameters":{"handleId":"source-layer"}},{"frame":1,"type":"layer.hide","parameters":{"handleId":"source-layer"}}]}}""");
        var target = Parameters("""{"timeline":{"playbackHandleId":"target-play","events":[]}}""");
        var snapshot = EntryPresetParameters.Capture(catalog, BuiltinEntryIds.PlayTimeline, source);
        Assert.That(snapshot["timeline"].TryGetProperty("playbackHandleId", out _), Is.False);
        var result = EntryPresetParameters.Apply(catalog, BuiltinEntryIds.PlayTimeline, snapshot, target)["timeline"];
        Assert.That(result.GetProperty("playbackHandleId").GetString(), Is.EqualTo("target-play"));
        var events = result.GetProperty("events");
        var id = events[0].GetProperty("parameters").GetProperty("handleId").GetString();
        Assert.That(id, Is.Not.EqualTo("source-layer"));
        Assert.That(events[1].GetProperty("parameters").GetProperty("handleId").GetString(), Is.EqualTo(id));
    }
}
