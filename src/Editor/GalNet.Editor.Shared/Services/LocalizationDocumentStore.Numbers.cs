using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Editor.Abstraction.Changes;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public sealed partial class LocalizationDocumentStore
{
    /// <summary>Durably reserves an identity. Reservations intentionally survive content undo; gaps are harmless.</summary>
    public async Task<string> ReserveCharacterKeyAsync()
    {
        RequireActive();
        var metadata = CaptureFile(LocalizationJson.MetadataPath);
        var catalog = CaptureFile(LocalizationJson.CatalogPath);
        LocalizationJson.Validate(metadata.Path, metadata.Data);
        LocalizationJson.Validate(catalog.Path, catalog.Data);
        long reserved = 0;
        await content.PublishStructureAsync((working, saved) =>
        {
            RequireActive();
            var currentMetadata = Current(working, metadata);
            var savedMetadata = Current(saved, metadata);
            var currentCatalog = Current(working, catalog);
            LocalizationJson.Validate(metadata.Path, currentMetadata);
            LocalizationJson.Validate(metadata.Path, savedMetadata);
            LocalizationJson.Validate(catalog.Path, currentCatalog);
            var high = Math.Max(HighWater(currentMetadata), HighWater(savedMetadata));
            using var json = JsonDocument.Parse(LocalizationJson.Decode(currentCatalog));
            foreach (var character in json.RootElement.EnumerateArray())
            {
                var id = character.GetProperty("id").GetString()!;
                if (id.StartsWith("character_", StringComparison.Ordinal) && long.TryParse(id.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    high = Math.Max(high, number);
            }
            reserved = checked(high + 1);
            var files = new ProjectFilePlan();
            if (!metadata.Exists) files.ExpectedHashes[metadata.Path] = null;
            return new NumberReservation(metadata, reserved, files);
        });
        return "character_" + reserved.ToString(CultureInfo.InvariantCulture);
    }

    private static long HighWater(byte[] bytes) => JsonNode.Parse(LocalizationJson.Decode(bytes))!["characterHighWater"]!.GetValue<long>();
    private static byte[] Current(EditorProjectDocument document, LocalizationFileSnapshot file)
    {
        var live = document.Files.Values.SingleOrDefault(item => item.Exists && ProjectFilePaths.Comparer.Equals(item.Path, file.Path));
        if (live is null && !file.Exists) return file.Data;
        if (live is null || live.Id != file.Id || !live.ManagedContent) throw ProjectFilePaths.Error("changed", file.Path);
        return live.Data;
    }

    private sealed record NumberReservation(LocalizationFileSnapshot Before, long Number, ProjectFilePlan Files) : IProjectFileStructureChange
    {
        public void Apply(EditorProjectDocument document, AuthorDocumentVersion version)
        {
            var bytes = Current(document, Before);
            var node = JsonNode.Parse(LocalizationJson.Decode(bytes))!;
            node["characterHighWater"] = Math.Max(Number, HighWater(bytes));
            if (!document.Files.TryGetValue(Before.Id, out var file))
                document.Files[Before.Id] = file = new() { Id = Before.Id, Path = Before.Path, ManagedContent = true };
            file.Exists = true;
            file.Data = LocalizationJson.WriteLike(bytes, node);
        }
    }
}
