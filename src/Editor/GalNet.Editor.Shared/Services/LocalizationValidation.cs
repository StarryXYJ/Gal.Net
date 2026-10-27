using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

public static class LocalizationValidation
{
    public static IReadOnlyList<ToolDiagnostic> ValidateDisk(string root, IEntryCatalog catalog)
    {
        var diagnostics = new List<ToolDiagnostic>();
        try
        {
            var reader = new ProjectRawReader(root);
            var settings = File.Exists(reader.Resolve("settings.json")) ? DirectProjectPersistence.LoadSettings(root) : new GalNet.Core.Configuration.ProjectConfiguration();
            var loaded = new EditorDocumentRepository(catalog, EditorDocumentValidation.Authoring).Load(root);
            var snapshot = new EditorProjectDocument { Graph = loaded.Document, GroupEntries = loaded.GroupEntries, Configuration = settings };
            var paths = Directory.Exists(reader.Resolve("I18n")) ? reader.Files("I18n", diagnostics) : [];
            var sources = paths.Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("I18n/External/", StringComparison.OrdinalIgnoreCase))
                .Select(path => new LocalizationSource(path, File.ReadAllText(reader.Resolve(path)))).ToArray();
            var index = new LocalizationReferenceIndex(snapshot, catalog, sources);
            foreach (var issue in index.Issues)
            {
                var file = issue.File; var pointer = issue.JsonPointer;
                var reference = index.References.FirstOrDefault(reference => reference.Owner == file && reference.JsonPointer == pointer && issue.Message.Contains("'" + reference.Key + "'", StringComparison.Ordinal));
                var entryId = issue.EntryId ?? reference?.EntryId;
                if (loaded.Document.Nodes.FirstOrDefault(node => node.Id == file) is { } owner)
                {
                    file = owner.Type == "Group" ? "Graph/" + owner.File : "Graph/graph.json";
                    if (entryId is not null && owner.Type == "Group" && loaded.GroupEntries[owner.Id].FindIndex(entry => entry.StableId == entryId) is var entryIndex && entryIndex >= 0)
                        pointer = "/entries/" + entryIndex + "/parameters" + pointer;
                    else if (entryId is not null && owner.Options is not null) pointer = "/nodes/" + loaded.Document.Nodes.IndexOf(owner) + "/options/" + owner.Options.FindIndex(option => option.Id == entryId) + pointer;
                }
                else if (file == "characters") file = LocalizationJson.CatalogPath;
                var incomplete = issue.Code is "schema.unavailable" or "field.unknown" or "field.opaque" or "analysis.limit";
                diagnostics.Add(new("localization." + issue.Code, incomplete ? "warning" : "error", issue.Message, file, entryId, pointer));
            }
            if (!index.Complete) diagnostics.Add(new("localization.incomplete", "warning", "Localization analysis is incomplete; provide all custom field schemas."));
            var options = settings.ReadDefaultOptions();
            if (options.AvailableLocales.Select(locale => locale.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.AvailableLocales.Count ||
                !options.AvailableLocales.Any(locale => locale.Code == options.TargetLocale.Code))
                diagnostics.Add(new("localization.languages", "error", "Project languages must be unique and include the development language.", "settings.json"));
            var metadata = reader.Resolve(LocalizationJson.MetadataPath);
            if (File.Exists(metadata)) LocalizationJson.Validate(LocalizationJson.MetadataPath, File.ReadAllBytes(metadata));
        }
        catch (Exception error) when (error is IOException or ArgumentException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { diagnostics.Add(new("localization.invalid", "error", error.Message)); }
        return diagnostics;
    }
}
