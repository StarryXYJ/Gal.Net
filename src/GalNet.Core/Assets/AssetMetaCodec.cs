using System.Text.Json;

namespace GalNet.Core.Assets;

/// <summary>Two-step metadata codec selected by stable type IDs, never CLR type names.</summary>
public sealed class AssetMetaCodec(IResourceTypeCatalog resourceTypes, JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public AssetMeta Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("type", out var discriminator) || discriminator.ValueKind != JsonValueKind.String) throw new InvalidDataException("Asset metadata requires a string 'type' discriminator.");
        var typeId = resourceTypes.NormalizeTypeId(discriminator.GetString()!);
        var registration = resourceTypes.Get(typeId);
        var meta = (AssetMeta?)JsonSerializer.Deserialize(json, registration.MetaDtoType, _options) ?? throw new InvalidDataException($"Metadata for '{typeId}' is empty.");
        if (!StringComparer.Ordinal.Equals(resourceTypes.NormalizeTypeId(meta.TypeId), typeId)) throw new InvalidDataException($"Metadata discriminator '{meta.TypeId}' does not match '{typeId}'.");
        if (string.IsNullOrWhiteSpace(meta.Id) || string.IsNullOrWhiteSpace(meta.Path)) throw new InvalidDataException("Asset metadata requires non-empty 'id' and 'path'.");
        meta.TypeId = typeId; meta.Id = meta.Id.Trim(); meta.Path = meta.Path.Trim();
        return meta;
    }
    public string Serialize(AssetMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        var registration = resourceTypes.Get(meta.TypeId);
        if (!registration.MetaDtoType.IsInstanceOfType(meta)) throw new InvalidDataException($"Metadata for '{meta.TypeId}' must use '{registration.MetaDtoType.Name}'.");
        return JsonSerializer.Serialize(meta, registration.MetaDtoType, _options);
    }
}
