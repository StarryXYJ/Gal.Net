using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Serialization;

namespace GalNet.Runtime.Loader;

/// <summary>Loads compiled generic primitive envelopes into a Runtime group.</summary>
public static class GalgroupLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void LoadIntoGroup(Group group, string galgroupPath) =>
        LoadIntoGroupFromContent(group, File.ReadAllText(galgroupPath));

    public static void LoadIntoGroupFromContent(Group group, string content)
    {
        ArgumentNullException.ThrowIfNull(group);

        CompiledGroupDocument document;
        try
        {
            document = JsonSerializer.Deserialize<CompiledGroupDocument>(content, JsonOptions)
                ?? throw new InvalidDataException("The .galgroup document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(".galgroup files must use the compiled primitive-envelope format.", exception);
        }

        if (document.Version != GroupDocument.CurrentVersion)
            throw new InvalidDataException($"Unsupported .galgroup version '{document.Version}'.");
        if (document.Kind != GroupDocumentKind.Compiled)
            throw new InvalidDataException("Runtime only accepts compiled .galgroup documents. Compile the .rawgalgroup source first.");

        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<Entry>(document.Entries.Count);
        for (var index = 0; index < document.Entries.Count; index++)
        {
            var source = document.Entries[index];
            if (string.IsNullOrWhiteSpace(source.Id) || !stableIds.Add(source.Id))
                throw new InvalidDataException($"Entry #{index + 1} must have a unique non-empty id.");
            if (!IsValidTypeId(source.TypeId))
                throw new InvalidDataException($"Entry '{source.Id}' has invalid primitive type ID '{source.TypeId}'.");
            if (source.Arguments.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Entry '{source.Id}' arguments must be a JSON object.");

            entries.Add(new PrimitiveEntry(source.TypeId, source.Arguments, source.BatchId)
            {
                Id = index + 1,
                Condition = source.Condition
            });
        }

        group.Entries.Clear();
        group.Entries.AddRange(entries);
    }

    private static bool IsValidTypeId(string typeId)
    {
        var separator = typeId.IndexOf('.');
        return separator > 0 && separator < typeId.Length - 1 &&
               typeId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }
}
