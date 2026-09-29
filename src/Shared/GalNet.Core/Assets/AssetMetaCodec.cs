using System.Text.Json;

namespace GalNet.Core.Assets;

/// <summary>Two-step metadata codec selected by stable type IDs, never CLR type names.</summary>
public sealed class AssetMetaCodec(IResourceTypeCatalog resourceTypes, JsonSerializerOptions? options = null)
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public AssetMeta Deserialize(string json, string? pathHint = null)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException exception) { throw new InvalidDataException("Asset metadata is not valid JSON.", exception); }

        using (document)
        {
        if (document.RootElement.ValueKind is not JsonValueKind.Object) throw new InvalidDataException("Asset metadata must be a JSON object.");
        var typeId = ReadTypeId(document.RootElement, pathHint);
        var registration = resourceTypes.Get(typeId);
        var meta = DeserializeOrDefault(json, registration, typeId);
        var id = ReadRequiredString(document.RootElement, "id");
        var path = ReadOptionalString(document.RootElement, "path") ?? pathHint;
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Asset metadata requires a non-empty 'path' or a provider path hint.");
        meta.TypeId = typeId;
        meta.Id = id.Trim();
        meta.Path = path.Trim().Replace('\\', '/');
        return meta;
        }
    }
    public string Serialize(AssetMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        var registration = resourceTypes.Get(meta.TypeId);
        if (!registration.MetaDtoType.IsInstanceOfType(meta)) throw new InvalidDataException($"Metadata for '{meta.TypeId}' must use '{registration.MetaDtoType.Name}'.");
        return JsonSerializer.Serialize(meta, registration.MetaDtoType, _options);
    }

    private string ReadTypeId(JsonElement root, string? pathHint)
    {
        var declared = ReadOptionalString(root, "type");
        if (!string.IsNullOrWhiteSpace(declared)) return resourceTypes.NormalizeTypeId(declared);
        var extension = Path.GetExtension(pathHint ?? "");
        if (resourceTypes.TryGetByExtension(extension, out var inferred)) return inferred.TypeId;
        return "data";
    }

    private AssetMeta DeserializeOrDefault(string json, ResourceTypeRegistration registration, string typeId)
    {
        try
        {
            return (AssetMeta?)JsonSerializer.Deserialize(json, registration.MetaDtoType, _options)
                ?? throw new InvalidDataException($"Metadata for '{typeId}' is empty.");
        }
        catch (JsonException)
        {
            return (AssetMeta)(Activator.CreateInstance(registration.MetaDtoType)
                ?? throw new InvalidDataException($"Metadata type '{registration.MetaDtoType.Name}' cannot be created."));
        }
    }

    private static string ReadRequiredString(JsonElement root, string name) =>
        ReadOptionalString(root, name) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Asset metadata requires a non-empty '{name}'.");

    private static string? ReadOptionalString(JsonElement root, string name)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, name) && property.Value.ValueKind is JsonValueKind.String)
                return property.Value.GetString();
        }
        return null;
    }
}
