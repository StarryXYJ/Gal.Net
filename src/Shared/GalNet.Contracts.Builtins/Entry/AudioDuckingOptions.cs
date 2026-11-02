using System.Text.Json.Serialization;
using GalNet.Contracts.Builtins.Audio;
using GalNet.Core.Parameters;

namespace GalNet.Contracts.Builtins;

[GenerateFieldSchema]
public partial class AudioDuckingOptions
{
    [Field(0)] public string InstanceId { get; set; } = "voice-duck";
    [Common, Field(1)] public string SourceTrackId { get; set; } = "vocal";
    [Common, Field(2)] public string TargetId { get; set; } = "bgm";
    [Field(3), JsonConverter(typeof(AudioTargetScopeValueConverter))] public SEffectScope Scope { get; set; } = SEffectScope.Bus;
    [Field(4, Minimum = 0, Maximum = 1)] public float Gain { get; set; } = .35f;
    [Field(5, Minimum = 1, Maximum = 5000)] public float AttackMs { get; set; } = 80;
    [Field(6, Minimum = 1, Maximum = 10000)] public float ReleaseMs { get; set; } = 400;
    [Field(7)] public bool Enabled { get; set; } = true;
}
