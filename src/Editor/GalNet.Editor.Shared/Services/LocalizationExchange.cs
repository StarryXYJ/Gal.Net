using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Services;

public sealed record LocalizationExchangeRow(string Key, string? SourceText, string? SourceFingerprint, string[] Context, string? Translation);
public sealed record LocalizationExchangeDocument(int Version, string ProjectId, string SourceLocale, string TargetLocale, LocalizationExchangeRow[] Rows, [property: JsonRequired] bool Complete);
public sealed record LocalizationExchangeInput(string? ProjectId, string? SourceLocale, string TargetLocale, LocalizationExchangeRow[] Rows);

/// <summary>Current JSON exchange format. Order and source text are never used as row identity.</summary>
public static class LocalizationExchange
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public static string Culture(string value)
    {
        var code = CultureInfo.GetCultureInfo(value).Name;
        return code.Length == 0 ? throw new InvalidDataException("Invariant culture is not an exchange language.") : code;
    }
    public static string Write(LocalizationExchangeDocument document) => JsonSerializer.Serialize(document, Options);
    public static LocalizationExchangeInput Parse(string json, string flatLanguage)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024) throw new InvalidDataException("Exchange file exceeds 8 MiB.");
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected an exchange object.");
        var duplicates = new List<ToolDiagnostic>(); ProjectValidationService.DuplicateProperties(root, "exchange", "", duplicates);
        if (duplicates.Count > 0) throw new InvalidDataException("Duplicate JSON properties.");
        LocalizationExchangeInput input;
        if (root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number)
        {
            var document = root.Deserialize<LocalizationExchangeDocument>(Options) ?? throw new InvalidDataException("Missing template.");
            if (document.Version != 1 || !Guid.TryParseExact(document.ProjectId, "N", out _) || document.Rows is null)
                throw new InvalidDataException("Unsupported template version or project identity.");
            input = new(Guid.Parse(document.ProjectId).ToString("N"), Culture(document.SourceLocale), Culture(document.TargetLocale), document.Rows);
        }
        else
            input = new(null, null, Culture(flatLanguage), root.EnumerateObject().Select(property => new LocalizationExchangeRow(property.Name, null, null, [],
                property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : throw new InvalidDataException("Flat translations must be strings or null."))).ToArray());
        if (input.Rows.Length > 100_000 || input.Rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.Key) || row.Context is null || row.Context.Any(context => context is null)) ||
            input.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != input.Rows.Length)
            throw new InvalidDataException("Duplicate, empty or invalid exchange rows.");
        foreach (var row in input.Rows)
        {
            if (input.ProjectId is not null && (row.SourceText is null ? row.SourceFingerprint is not null :
                row.SourceFingerprint is null || !string.Equals(LocalizationTextState.Fingerprint(row.SourceText), row.SourceFingerprint, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Source fingerprint does not match the exported source text.");
        }
        return input;
    }
}
