using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Scene;
using GalNet.Core.Serialization;

namespace GalNet.Runtime.Loader;

/// <summary>Loads JSON .galgroup authoring documents and compiles them into Runtime entries.</summary>
public static class GalgroupLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void LoadIntoGroup(Group group, string galgroupPath) =>
        LoadIntoGroupFromContent(group, File.ReadAllText(galgroupPath));

    public static void LoadIntoGroupFromContent(Group group, string content)
    {
        GroupDocument document;
        try
        {
            document = JsonSerializer.Deserialize<GroupDocument>(content, JsonOptions)
                ?? throw new InvalidDataException("The .galgroup document is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(".galgroup files must use the JSON GroupDocument format.", exception);
        }

        if (document.Version != 1)
            throw new InvalidDataException($"Unsupported .galgroup version '{document.Version}'.");

        var stableIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<Entry>(document.Entries.Count);
        for (var index = 0; index < document.Entries.Count; index++)
        {
            var source = document.Entries[index];
            if (string.IsNullOrWhiteSpace(source.Id) || !stableIds.Add(source.Id))
                throw new InvalidDataException($"Entry #{index + 1} must have a unique non-empty id.");
            if (string.IsNullOrWhiteSpace(source.Type))
                throw new InvalidDataException($"Entry '{source.Id}' has no type.");

            try
            {
                entries.Add(EntryRegistry.Create(source.Type, index + 1, source.Condition, CompileParameters(source)));
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
            {
                throw new InvalidDataException($"Invalid entry '{source.Id}' ({source.Type}): {exception.Message}", exception);
            }
        }

        group.Entries.Clear();
        group.Entries.AddRange(entries);
    }

    private static IReadOnlyDictionary<string, string> CompileParameters(GroupEntryDocument source)
    {
        if (source.Type == ShowLayerEntry.TypeId) return CompileLayerShow(source.Parameters);
        if (source.Type == HideLayerEntry.TypeId) return Rename(source.Parameters, ("handleId", "handleId"));
        if (source.Type == MoveLayerEntry.TypeId) return CompileLayerMove(source.Parameters);
        if (source.Type == ReplaceLayerEntry.TypeId) return Rename(source.Parameters, ("handleId", "handleId"), ("assetId", "assetId"));
        return source.Parameters.ToDictionary(pair => pair.Key, pair => ToValue(pair.Value), StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> CompileLayerShow(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var compiled = Rename(parameters,
            ("handleId", "handleId"), ("assetId", "assetId"), ("z", "z"), ("opacity", "opacity"), ("displayMode", "displayMode"),
            ("transitionId", "transitionId"), ("transitionDuration", "transitionDuration"), ("transitionBlocking", "transitionBlocking"),
            ("transitionParameters", "transitionParameters"));
        compiled["transform"] = ReadTransform(parameters);
        Require(compiled, "handleId");
        Require(compiled, "assetId");
        if (compiled.TryGetValue("displayMode", out var displayMode) &&
            !Enum.TryParse<LayerDisplayMode>(displayMode, true, out _))
            throw new InvalidDataException($"Unknown layer displayMode '{displayMode}'.");
        return compiled;
    }

    private static IReadOnlyDictionary<string, string> CompileLayerMove(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        var compiled = Rename(parameters, ("handleId", "handleId"), ("z", "z"), ("duration", "duration"));
        compiled["transform"] = ReadTransform(parameters);
        Require(compiled, "handleId");
        return compiled;
    }

    private static Dictionary<string, string> Rename(IReadOnlyDictionary<string, JsonElement> parameters, params (string Source, string Target)[] names)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, target) in names)
            if (parameters.TryGetValue(source, out var value)) values[target] = ToValue(value);
        return values;
    }

    private static string ReadTransform(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        if (!parameters.TryGetValue("transform", out var value)) return "{}";
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("'transform' must be an object.");
        return value.GetRawText();
    }

    private static string ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        JsonValueKind.Null => "",
        _ => throw new InvalidDataException("Parameter values cannot be undefined.")
    };

    private static void Require(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"'{name}' is required.");
    }
}
