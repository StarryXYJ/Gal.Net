using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins.Scene;

[GenerateFieldSchema]
public sealed partial class TimelineAudioTrackDefinition
{
    public TimelineAudioTrackDefinition() => Keys = [];
    [Field(0), JsonConverter(typeof(StringEnumValueConverter<SEffectScope>))] public SEffectScope Scope { get; set; }
    [Field(1)] public string TargetId { get; set; } = "";
    /// <summary>Empty selects the target's story mixer gain. Otherwise selects a directly attached effect.</summary>
    [Field(2)] public string InstanceId { get; set; } = "";
    [Field(3)] public string Parameter { get; set; } = "gain";
    [Field(4, DefaultJson = "[]")] public List<TimelineKeyframeDefinition> Keys { get; set; }
    [JsonIgnore] public string Identity => GalNet.Core.Serialization.JsonCodec.Serialize(new[] { Scope.ToString(), TargetId, InstanceId, Parameter });
}
