using System.Globalization;
using System.Text.Json;
using GalNet.Core.I18n;
using GalNet.Runtime.I18n;

namespace GalNet.GameHost.Content;

/// <summary>Host-ready text sources and language-independent character metadata.</summary>
public sealed class GameLocalizationContent
{
    public string DefaultCulture { get; init; } = "en-US";
    public CharacterCatalog Characters { get; init; } = CharacterCatalog.Empty;
    public IReadOnlyList<GameLocalizationSource> Sources { get; init; } = [];
    public IReadOnlyList<GameLocalizationSource> ExternalSources { get; init; } = [];
    public IReadOnlyList<GameLocalizationDiagnostic> Diagnostics { get; init; } = [];

    public GameTextResolver CreateResolver(Func<CultureInfo> currentCulture,
        IEnumerable<LocalizedTextSource>? bootstrap = null, IEnumerable<GameLocalizationSource>? additional = null) =>
        new((bootstrap ?? []).Concat(Sources.Concat(ExternalSources).Concat(additional ?? [])
            .Select(source => source.ToTextSource())), CultureInfo.GetCultureInfo(DefaultCulture), currentCulture);
}

/// <summary>One source document; hosts load each document into their localization provider in order.</summary>
public sealed record GameLocalizationSource(string Culture, string RelativePath, string Json)
{
    public LocalizedTextSource ToTextSource()
    {
        var text = ReadText();
        return new(CultureInfo.GetCultureInfo(Culture), key => text.GetValueOrDefault(key));
    }

    public IReadOnlyDictionary<string, string> ReadText()
    {
        using var document = JsonDocument.Parse(Json);
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        Read(document.RootElement, "");
        return text;

        void Read(JsonElement element, string prefix)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Text sources require objects with string leaves.");
            foreach (var property in element.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name)) throw new InvalidDataException("Localization keys cannot be empty.");
                var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                if (property.Value.ValueKind == JsonValueKind.Object) Read(property.Value, key);
                else if (property.Value.ValueKind == JsonValueKind.String) text[key] = property.Value.GetString()!;
                else throw new InvalidDataException($"Localization key '{key}' must have a string value.");
            }
        }
    }
}

public sealed record GameLocalizationDiagnostic(string RelativePath, string Message);
