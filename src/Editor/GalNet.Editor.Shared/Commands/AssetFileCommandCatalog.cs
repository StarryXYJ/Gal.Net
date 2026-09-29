using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Commands;

public sealed class AssetFileCommandCatalog : IProjectFileCommandCatalog
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyList<IProjectCommandDefinition> _definitions =
    [
        Define<ImportAssetsCommand>("asset.import", "Imports files into the project asset directory."),
        Define<CreateAssetDirectoryCommand>("asset.directory.create", "Creates a directory below the project asset root."),
        Define<MoveAssetCommand>("asset.move", "Moves an asset or asset directory within the project asset root."),
        Define<RenameAssetCommand>("asset.rename", "Renames an asset and optionally updates project references."),
        Define<DeleteAssetCommand>("asset.delete", "Deletes an asset after returning its project references."),
        Define<PatchAssetMetadataCommand>("asset.meta.patch", "Updates supported asset metadata such as filter and compression mode.")
    ];

    public IReadOnlyList<IProjectCommandDefinition> GetAll() => _definitions;

    public IProjectFileCommand Deserialize(string commandId, JsonElement payload, JsonSerializerOptions? options = null)
    {
        var definition = _definitions.FirstOrDefault(item => string.Equals(item.Id, commandId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Unknown asset command '{commandId}'.");
        return (IProjectFileCommand?)payload.Deserialize(definition.CommandType, options ?? DefaultOptions)
            ?? throw new JsonException($"Asset command '{commandId}' deserialized to null.");
    }

    private static IProjectCommandDefinition Define<T>(string id, string description) where T : IProjectFileCommand
    {
        var parameters = typeof(T).GetConstructors().OrderByDescending(item => item.GetParameters().Length).First().GetParameters()
            .Select(parameter => new EditorCommandParameter(
                parameter.Name ?? "value",
                FriendlyName(parameter.ParameterType),
                !parameter.HasDefaultValue && Nullable.GetUnderlyingType(parameter.ParameterType) is null,
                $"Value for {parameter.Name}."))
            .ToList();
        return new Definition(id, description, new I18nKey("Command." + id), typeof(T), new EditorCommandSchema(parameters));
    }

    private static string FriendlyName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string)) return "array";
        return type.Name;
    }

    private sealed record Definition(
        string Id,
        string Description,
        I18nKey DisplayNameKey,
        Type CommandType,
        EditorCommandSchema Schema) : IProjectCommandDefinition;
}
