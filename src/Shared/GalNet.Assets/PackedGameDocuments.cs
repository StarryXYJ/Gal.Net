using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Assets;
using GalNet.Core.Serialization;

namespace GalNet.Assets;

/// <summary>Internal documents use GPAK with a fixed catalog, separate from game resource overrides.</summary>
public static class PackedGameDocuments
{
    public const string GameFileName = "game.pak";
    public const string LocalizationFileName = "localization.pak";
    public const string InstallationPath = "installation.json";
    private const string DocumentType = "document";
    private const string IdPrefix = "galnet:document:";
    private static readonly IResourceTypeCatalog Types = new ResourceTypeCatalogBuilder()
        .Add<AssetMeta>(DocumentType).WithoutDependencies(DocumentType).Build();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Build(string projectId, string buildId, IEnumerable<KeyValuePair<string, byte[]>> documents)
    {
        var files = documents.Select(pair =>
        {
            FileSystemPaths.ValidateRelativeFile(pair.Key);
            var metadata = new AssetMeta { Id = IdPrefix + pair.Key, Path = pair.Key, TypeId = DocumentType };
            return (IGameFile)new GameFile(metadata.Id, metadata.Path, DocumentType, metadata, pair.Value);
        }).ToArray();
        return PakBuilder.Build("documents", files, Types, descriptor: new() { ProjectId = projectId, BuildId = buildId });
    }

    public static PackedDocumentSet Read(byte[] bytes)
    {
        using var archive = Archive.Deserialize("documents", bytes, Types);
        var descriptor = archive.Descriptor;
        if (descriptor.Kind != "base" || descriptor.PartIndex != 0 || descriptor.PartCount != 1
            || string.IsNullOrWhiteSpace(descriptor.ProjectId) || !IsHash(descriptor.BuildId))
            throw new InvalidDataException("Invalid internal document package identity.");
        var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.MetadataIndex)
        {
            FileSystemPaths.ValidateRelativeFile(entry.Path);
            if (entry.Id != IdPrefix + entry.Path || entry.TypeId != DocumentType)
                throw new InvalidDataException("Invalid internal document identity.");
            documents.Add(entry.Path, archive.GetAsset(entry.Id)!.ReadAllBytes());
        }
        return new(descriptor, documents);
    }

    public static PackedGamePackage ReadGame(byte[] bytes)
    {
        var documents = Read(bytes);
        var manifest = JsonCodec.Deserialize<GameContentManifest>(documents.GetText(GameContentManifest.FileName), JsonOptions)
            ?? throw new InvalidDataException("Missing packed content descriptor.");
        if (manifest.Version != 3 || string.IsNullOrWhiteSpace(manifest.ProjectId) || !IsHash(manifest.BuildId)
            || manifest.ProjectId != documents.Descriptor.ProjectId || manifest.BuildId != documents.Descriptor.BuildId
            || manifest.BasePaks is not { Count: > 0 } || manifest.Stories is null
            || manifest.Stories.Any(story => story is null || string.IsNullOrWhiteSpace(story.PackageId) || story.Parts is not { Count: > 0 }
                || story.Parts.Any(file => file?.Path is null || !file.Path.StartsWith("StoryPatches/" + manifest.BuildId + "/", StringComparison.Ordinal)))
            || manifest.BasePaks.Any(file => file?.Path is null || !file.Path.StartsWith("Assets/Paks/", StringComparison.Ordinal))
            || manifest.LocalizationPak is not { Path: LocalizationFileName } language || !ValidFile(language))
            throw new InvalidDataException("Invalid packed content descriptor.");
        var index = JsonCodec.Deserialize<GalpakManifest>(documents.GetText(InstallationPath), JsonOptions)
            ?? throw new InvalidDataException("Missing packed installation index.");
        var expected = manifest.BasePaks.Append(language)
            .Concat(manifest.Stories.SelectMany(story => story.Parts)).ToArray();
        if (index.Version != 4 || index.ProjectId != manifest.ProjectId || index.Files is null
            || expected.Any(file => !ValidFile(file)) || expected.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Length
            || index.Files.Count != expected.Length || index.Files.Distinct().Count() != expected.Length
            || expected.Any(file => !index.Files.Contains(file)))
            throw new InvalidDataException("Packed installation index differs from content descriptor.");
        foreach (var required in new[] { "settings.json", "gallery.json", "characters.json", "packaging-layout.json" })
            _ = documents.GetText(required);
        if (documents.Documents.Count != 6) throw new InvalidDataException("Unexpected internal game document.");
        return new(documents, manifest, index);
    }

    public static IEnumerable<string> ExtensionDirectories(string buildId)
    {
        if (!IsHash(buildId)) throw new InvalidDataException("Invalid content build identity.");
        return ["I18n", "ResourcePatches", "StoryPatches", "StoryPatches/" + buildId];
    }

    private static bool ValidFile(GalpakFileEntry? file) => file is not null && file.Size >= 0 && IsHash(file.Sha256);
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed record PackedDocumentSet(PakDescriptor Descriptor, IReadOnlyDictionary<string, byte[]> Documents)
{
    public string GetText(string path)
    {
        if (!Documents.TryGetValue(path, out var bytes)) throw new InvalidDataException($"Packed document '{path}' was not found.");
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException($"Packed document '{path}' is not UTF-8.", error); }
    }
}

public sealed record PackedGamePackage(PackedDocumentSet Documents, GameContentManifest Manifest, GalpakManifest Installation);
