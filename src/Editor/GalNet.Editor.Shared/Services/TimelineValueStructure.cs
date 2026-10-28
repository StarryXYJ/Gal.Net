using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Parameters;

namespace GalNet.Editor.Shared.Services;

/// <summary>Runtime DTO fields plus author-only timeline metadata; event payloads use their entry contract.</summary>
internal static class TimelineValueStructure
{
    public const string EventParameters = "timeline.event.parameters";
    private static readonly JsonSerializerOptions JsonOptions = BuiltinJson.CreateOptions(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, stringEnums: true);
    private static readonly ValueStructure Tangents = ValueStructure.Object([
        new("incoming", new(typeof(string))), new("outgoing", new(typeof(string))), new("linked", new(typeof(bool)))], ignoresCase: true);
    private static readonly ValueStructure Key = ValueStructure.Object(TimelineKeyframeDefinition.Schema.Concat([
        new FieldDescriptor("editorTangents", new ValueDescriptor(typeof(JsonElement)).WithStructure(Tangents))]), ignoresCase: true);
    private static readonly ValueStructure Track = ValueStructure.Object(TimelineTrackDefinition.Schema.Select(field => field.Id == "keys"
        ? Structure(field, List(Key)) : field), ignoresCase: true);
    private static readonly ValueStructure Event = ValueStructure.Object(TimelineEventDefinition.Schema.Select(field => field.Id == "parameters"
        ? Structure(field, ValueStructure.Dynamic(EventParameters)) : field).Concat([new FieldDescriptor("editorId", new(typeof(string)))]), ignoresCase: true);
    private static readonly ValueStructure Timeline = ValueStructure.Object(TimelineDefinition.Schema.Select(field => field.Id switch
    { "tracks" => Structure(field, List(Track)), "events" => Structure(field, List(Event)), _ => field }), ignoresCase: true);

    public static FieldDescriptor Describe(FieldDescriptor field) => new(field.Id,
        field.Value.WithJsonOptions(JsonOptions).WithStructure(Timeline),
        field.IsRequired, field.DefaultValue, field.Editor, field.LabelKey, field.DescriptionKey, field.Semantic, field.ResourceTypeId);
    private static ValueStructure List(ValueStructure element) => ValueStructure.Sequence(new("element", new ValueDescriptor(typeof(JsonElement)).WithStructure(element)));
    private static FieldDescriptor Structure(FieldDescriptor field, ValueStructure structure) => new(field.Id, field.Value.WithStructure(structure),
        field.IsRequired, field.DefaultValue, field.Editor, field.LabelKey, field.DescriptionKey, field.Semantic, field.ResourceTypeId);
}
