using GalNet.Core.Serialization;
using System.Globalization;
using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.I18n;
using GalNet.GameHost.Content;

namespace GalNet.Storage.FileSystem;

/// <summary>Reads package text sources without referencing a concrete localization library.</summary>
public static class GameLocalizationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static GameLocalizationContent Load(string gameDirectory, Graph graph, string? defaultCulture = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetFullPath(gameDirectory), "I18n");
        var charactersPath = Path.Combine(root, "catalog", "characters.json");
        var characters = CharacterCatalog.Empty;
        if (File.Exists(charactersPath))
        {
            RejectLink(root);
            RejectLink(Path.GetDirectoryName(charactersPath)!);
            RejectLink(charactersPath);
            try
            {
                var definitions = JsonCodec.Deserialize<List<CharacterDefinition>>(File.ReadAllText(charactersPath), JsonOptions)
                    ?? throw new InvalidDataException("Character catalog must be a JSON array.");
                characters = new CharacterCatalog(definitions);
            }
            catch (Exception error) when (error is JsonException or ArgumentException)
            {
                throw new InvalidDataException($"Invalid character catalog '{charactersPath}'.", error);
            }
        }

        var sources = new List<GameLocalizationSource>();
        var defaultKeys = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(root))
        {
            RejectLink(root);
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (var path in Directory.EnumerateFiles(root, "*.json", options)
                         .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var cultureDirectory = relative.Split('/')[0];
                if (cultureDirectory.Equals("catalog", StringComparison.OrdinalIgnoreCase)
                    || cultureDirectory.Equals("External", StringComparison.OrdinalIgnoreCase)) continue;
                if (!relative.Contains('/')) throw new InvalidDataException($"Localization file '{relative}' must be below a culture directory.");
                var culture = CultureInfo.GetCultureInfo(cultureDirectory);
                var json = File.ReadAllText(path);
                try
                {
                    var text = new GameLocalizationSource(culture.Name, relative, json).ReadText();
                    if (string.Equals(culture.Name, defaultCulture, StringComparison.OrdinalIgnoreCase))
                        defaultKeys.UnionWith(text.Keys);
                }
                catch (JsonException error)
                {
                    throw new InvalidDataException($"Invalid localization file '{relative}'.", error);
                }
                sources.Add(new(culture.Name, relative, json));
            }
        }

        if (defaultCulture is not null) ValidateRequiredKeys(graph, characters, defaultKeys, defaultCulture);
        var diagnostics = new List<GameLocalizationDiagnostic>();
        var externalSources = LoadExternal(Path.Combine(root, "External"), diagnostics, cancellationToken);
        return new GameLocalizationContent
        {
            DefaultCulture = defaultCulture ?? "en-US",
            Characters = characters,
            Sources = sources.AsReadOnly(),
            ExternalSources = externalSources,
            Diagnostics = diagnostics.AsReadOnly()
        };
    }

    private static IReadOnlyList<GameLocalizationSource> LoadExternal(string root,
        List<GameLocalizationDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var sources = new List<GameLocalizationSource>();
        if (!Directory.Exists(root)) return sources;
        try
        {
            RejectLink(root);
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
            foreach (var path in Directory.EnumerateFiles(root, "*.json", options)
                         .OrderBy(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                try
                {
                    if (!relative.Contains('/')) throw new InvalidDataException("External JSON must be below a culture directory.");
                    var culture = CultureInfo.GetCultureInfo(relative.Split('/')[0]);
                    var json = File.ReadAllText(path);
                    _ = new GameLocalizationSource(culture.Name, relative, json).ReadText();
                    sources.Add(new(culture.Name, $"External/{relative}", json));
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    diagnostics.Add(new($"External/{relative}", error.Message));
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("External", error.Message));
        }
        return sources.AsReadOnly();
    }

    private static void ValidateRequiredKeys(Graph graph, CharacterCatalog characters, HashSet<string> defaultKeys, string defaultCulture)
    {
        foreach (var character in characters.Characters) Require(character.DisplayNameKey);
        foreach (var group in graph.Nodes.OfType<Group>())
            foreach (var entry in group.Entries.OfType<PrimitiveEntry>())
            {
                if (entry.Type is not ("dialogue.text" or "nvl.append")) continue;
                if (entry.Type == "dialogue.text" && entry.Arguments.TryGetProperty("speaker", out var speaker)
                    && !string.IsNullOrWhiteSpace(speaker.GetString())) characters.GetRequired(speaker.GetString()!);
                Require(entry.Arguments.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "");
            }
        foreach (var branch in graph.Nodes.OfType<Branch>().Where(branch => branch.BranchType == BranchType.Choice))
            foreach (var option in branch.Options) Require(option.Text);

        void Require(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!defaultKeys.Contains(key))
                throw new InvalidDataException($"Default language '{defaultCulture}' is missing required localization key '{key}'.");
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Localization paths cannot be symbolic links: '{path}'.");
    }
}
