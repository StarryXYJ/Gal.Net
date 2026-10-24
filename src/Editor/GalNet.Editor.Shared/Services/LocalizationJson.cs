using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GalNet.Editor.Shared.Services;

/// <summary>Strict author-file parsing and targeted leaf changes. Unknown catalog/metadata members survive edits.</summary>
public static class LocalizationJson
{
    public const string CatalogPath = "I18n/catalog/characters.json";
    public const string MetadataPath = "Graph/localization-authoring.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Path(string path)
    {
        path = ProjectFilePaths.Normalize(path);
        if (path is CatalogPath or MetadataPath) return path;
        var parts = path.Split('/');
        if (parts.Length < 3 || parts[0] != "I18n" || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("External", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("catalog", StringComparison.OrdinalIgnoreCase))
            throw ProjectFilePaths.Error("protected", path);
        if (CultureInfo.GetCultureInfo(parts[1]).Name.Length == 0) throw ProjectFilePaths.Error("category", path);
        return path;
    }

    public static byte[] Initial(string path) => Encoding.UTF8.GetBytes(Path(path) switch
    {
        CatalogPath => "[]",
        MetadataPath => "{\"version\":1,\"characterHighWater\":0}",
        _ => "{}"
    });

    public static string Decode(byte[] bytes)
    {
        var (encoding, skip) = EncodingOf(bytes);
        return encoding.GetString(bytes, skip, bytes.Length - skip);
    }
    private static (Encoding Encoding, int Skip) EncodingOf(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (new UTF32Encoding(false, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return (new UTF32Encoding(true, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return (new UnicodeEncoding(false, true, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return (new UnicodeEncoding(true, true, true), 2);
        var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        return (new UTF8Encoding(bom, true), bom ? 3 : 0);
    }

    public static byte[] WriteLike(byte[] original, JsonNode node)
    {
        var (encoding, _) = EncodingOf(original);
        return encoding.GetPreamble().Concat(encoding.GetBytes(node.ToJsonString(JsonOptions))).ToArray();
    }

    public static void Validate(string path, byte[] bytes)
    {
        path = Path(path);
        using var json = JsonDocument.Parse(Decode(bytes));
        CheckProperties(json.RootElement);
        if (path == CatalogPath)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Character catalog must be an array.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var id = ProjectRawReader.Text(item, "id"); var name = ProjectRawReader.Text(item, "displayNameKey");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id)) throw new InvalidDataException("Invalid or duplicate character identity.");
            }
        }
        else if (path == MetadataPath)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
                !root.TryGetProperty("characterHighWater", out var water) || water.ValueKind != JsonValueKind.Number || !water.TryGetInt64(out var highWater) || highWater < 0)
                throw new InvalidDataException("Unsupported localization author metadata.");
            if (root.TryGetProperty("exchangeProjectId", out var projectId) && (projectId.ValueKind != JsonValueKind.String || !Guid.TryParseExact(projectId.GetString(), "N", out _)))
                throw new InvalidDataException("Invalid localization exchange project identity.");
            if (root.TryGetProperty("textStates", out var states))
            {
                if (states.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Text states must be an object.");
                foreach (var item in states.EnumerateObject())
                {
                    var state = item.Value;
                    if (string.IsNullOrWhiteSpace(item.Name) || state.ValueKind != JsonValueKind.Object || !state.TryGetProperty("sourceLocale", out var locale) || locale.ValueKind != JsonValueKind.String ||
                        !state.TryGetProperty("sourceFingerprint", out var fingerprint) || !Hash(fingerprint) ||
                        !state.TryGetProperty("translations", out var translations) || translations.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Invalid text revision state.");
                    if (CultureInfo.GetCultureInfo(locale.GetString()!).Name.Length == 0) throw new InvalidDataException("Source culture cannot be invariant.");
                    foreach (var translation in translations.EnumerateObject())
                    { if (CultureInfo.GetCultureInfo(translation.Name).Name.Length == 0 || translation.Value.ValueKind != JsonValueKind.Null && !Hash(translation.Value)) throw new InvalidDataException("Invalid translation revision."); }
                }
            }
        }
        else _ = TextLeaves(json.RootElement);
    }
    private static bool Hash(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is { Length: 64 } hash && hash.All(Uri.IsHexDigit);

    private static void CheckProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                CheckProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckProperties(item);
    }

    private static Dictionary<string, string[]> TextLeaves(JsonElement root)
    {
        var leaves = new Dictionary<string, string[]>(StringComparer.Ordinal);
        Walk(root, "", []);
        return leaves;
        void Walk(JsonElement value, string prefix, string[] path)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Translations require string leaves in an object.");
            foreach (var property in value.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name)) throw new InvalidDataException("Empty translation key.");
                var key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                var child = path.Append(property.Name).ToArray();
                if (property.Value.ValueKind == JsonValueKind.Object) Walk(property.Value, key, child);
                else if (property.Value.ValueKind != JsonValueKind.String || !leaves.TryAdd(key, child))
                    throw new InvalidDataException("Invalid or colliding translation key: " + key);
            }
        }
    }

    public static string? Text(byte[] bytes, string key)
    {
        using var json = JsonDocument.Parse(Decode(bytes));
        CheckProperties(json.RootElement);
        if (!TextLeaves(json.RootElement).TryGetValue(key, out var path)) return null;
        var value = json.RootElement;
        foreach (var part in path) value = value.GetProperty(part);
        return value.GetString();
    }

    public static byte[] SetText(byte[] bytes, string key, string text)
        => SetTexts(bytes, new Dictionary<string, string>(StringComparer.Ordinal) { [key] = text });

    public static IReadOnlyDictionary<string, string> Texts(byte[] bytes)
    {
        using var json = JsonDocument.Parse(Decode(bytes)); CheckProperties(json.RootElement);
        return TextLeaves(json.RootElement).ToDictionary(pair => pair.Key, pair =>
        {
            var value = json.RootElement; foreach (var part in pair.Value) value = value.GetProperty(part);
            return value.GetString()!;
        }, StringComparer.Ordinal);
    }
    public static byte[] SetTexts(byte[] bytes, IReadOnlyDictionary<string, string> values)
    {
        using var json = JsonDocument.Parse(Decode(bytes));
        CheckProperties(json.RootElement);
        var leaves = TextLeaves(json.RootElement);
        var root = JsonNode.Parse(Decode(bytes))!.AsObject();
        foreach (var (key, text) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key); ArgumentNullException.ThrowIfNull(text);
            var path = leaves.GetValueOrDefault(key) ?? [key]; var parent = root;
            foreach (var part in path.SkipLast(1)) parent = parent[part]!.AsObject();
            if (!leaves.ContainsKey(key) && parent.ContainsKey(path[^1])) throw new InvalidDataException("Key conflicts with an existing object.");
            parent[path[^1]] = text;
        }
        var result = WriteLike(bytes, root);
        using var check = JsonDocument.Parse(Decode(result));
        _ = TextLeaves(check.RootElement);
        return result;
    }
}
