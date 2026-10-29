using GalNet.Core.Assets;

namespace GalNet.Contracts.Builtins.Assets;

public sealed class SpriteAssetMeta : AssetMeta
{
    [System.Text.Json.Serialization.JsonPropertyName("filter")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Filter { get; set; }
}
