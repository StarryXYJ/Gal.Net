using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Services;

/// <summary>Plans dependency-owned PAKs in a private stage and publishes verified game content.</summary>
public static class GamePackageExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactOptions = new(JsonSerializerDefaults.Web);
    private sealed record Payload(string Path, string StagedPath, string Sha256, long Size)
    {
        public GalpakFileEntry Entry => new(Path, Sha256, Size);
    }
    private sealed record Prepared(Payload[] Base, Payload[] Stories, byte[] Layout, string BuildId);

    public static async Task<GameContentExportResult> ExportDirectoryAsync(string projectId, string projectName, string projectRoot,
        string outputDirectory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, IEntryCatalog entryCatalog,
        ProjectConfigurationSchema? configurations = null, CancellationToken cancellationToken = default)
    {
        string? stage = null;
        string? publish = null;
        try
        {
            var source = Path.GetFullPath(projectRoot);
            var target = Path.GetFullPath(outputDirectory);
            ProjectContentBuilder.ValidateOutputPath(source, target);
            if (Directory.Exists(target) || File.Exists(target)) throw new InvalidOperationException($"Export directory '{target}' already exists.");
            var parent = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(parent);
            stage = Path.Combine(parent, ".content-" + Guid.NewGuid().ToString("N"));
            publish = Path.Combine(parent, ".export-" + Guid.NewGuid().ToString("N"));
            var prepared = await PrepareAsync(projectId, projectName, source, stage, resourceTypes, galleryTypes, entryCatalog, configurations, cancellationToken);
            Directory.CreateDirectory(publish);
            foreach (var file in prepared.Base.Concat(prepared.Stories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Safe(publish, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Safe(stage, file.StagedPath), destination);
            }
            foreach (var directory in PackedGameDocuments.ExtensionDirectories(prepared.BuildId))
                Directory.CreateDirectory(Safe(publish, directory));
            await VerifyDirectoryAsync(publish, prepared.Base.Concat(prepared.Stories), resourceTypes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(publish, target);
            publish = null;
            return GameContentExportResult.Succeeded(target);
        }
        catch (OperationCanceledException) { return GameContentExportResult.Failed("Export canceled."); }
        catch (Exception error) when (ExportError(error)) { return GameContentExportResult.Failed(error.Message); }
        finally { Cleanup(stage); Cleanup(publish); }
    }

    /// <summary>Exports a standalone archive containing the base game and all declared story parts.</summary>
    public static async Task<GamePackageExportResult> ExportAsync(string projectId, string projectName, string projectRoot,
        string outputDirectory, IResourceTypeCatalog resourceTypes, IGalleryTypeCatalog galleryTypes, IEntryCatalog entryCatalog,
        ProjectConfigurationSchema? configurations = null, CancellationToken cancellationToken = default)
    {
        string? stage = null;
        string? temporary = null;
        try
        {
            var source = Path.GetFullPath(projectRoot);
            var output = Path.GetFullPath(outputDirectory);
            ProjectContentBuilder.ValidateOutputPath(source, output);
            Directory.CreateDirectory(output);
            stage = Path.Combine(output, ".content-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(output, SafeName(projectName) + ".galpak");
            temporary = target + ".export-" + Guid.NewGuid().ToString("N") + ".tmp";
            var prepared = await PrepareAsync(projectId, projectName, source, stage, resourceTypes, galleryTypes, entryCatalog, configurations, cancellationToken);
            var payloads = prepared.Base.Concat(prepared.Stories).ToArray();
            await using (var file = File.Create(temporary))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (var payload in payloads)
                {
                    var entry = zip.CreateEntry("pak/" + payload.Path, CompressionLevel.NoCompression);
                    await using var input = File.OpenRead(Safe(stage, payload.StagedPath));
                    await using var destination = entry.Open();
                    await input.CopyToAsync(destination, cancellationToken);
                }
                zip.CreateEntry("pak/");
                foreach (var directory in PackedGameDocuments.ExtensionDirectories(prepared.BuildId))
                    zip.CreateEntry("pak/" + directory + "/");
            }
            await VerifyZipAsync(temporary, payloads, resourceTypes, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(output, SafeName(projectName) + ".layout.json"), prepared.Layout, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
            temporary = null;
            return GamePackageExportResult.Succeeded(target);
        }
        catch (OperationCanceledException) { return GamePackageExportResult.Failed("Export canceled."); }
        catch (Exception error) when (ExportError(error)) { return GamePackageExportResult.Failed(error.Message); }
        finally { Cleanup(stage); if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<Prepared> PrepareAsync(string projectId, string projectName, string source, string stage,
        IResourceTypeCatalog types, IGalleryTypeCatalog galleryTypes, IEntryCatalog catalog, ProjectConfigurationSchema? configurations, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(galleryTypes);
        ArgumentNullException.ThrowIfNull(catalog);
        var (_, prepared) = await new ProjectContentBuilder(catalog, types, configurations).BuildPreparedAsync(source, stage, async (privateStage, token) =>
        {
            using var provider = new LocalFileProvider(Path.Combine(privateStage, "Assets"), types, optional: true);
            using var archive = await provider.OpenArchiveAsync("assets", token);
            var assets = archive.AssetIds.Select(id => archive.GetAsset(id)!).ToArray();
            var gallery = new GalleryCatalogCompiler(types, galleryTypes).Compile(assets.Select(file => file.Metadata));
            var story = StoryResourceCompiler.Compile(privateStage, catalog, token, assets);
            var available = assets.Concat(story).ToArray();
            var configuration = GalNet.Editor.Shared.Commands.DirectProjectPersistence.LoadSettings(privateStage);
            var options = configuration.Read(ContentPackagingOptions.Section);
            var roots = options.IncludeResources.Concat(gallery.Items.Select(item => item.ResourceId))
                .Concat(configuration.Read(ResourceOverrideOptions.Section).AdditionalOriginals)
                .Concat(configuration.ReadDefaultOptions().DefaultVisualPrograms.Values.Where(id => !string.IsNullOrWhiteSpace(id))).ToArray();
            var complete = options.DeclaredCompleteResources.Select(id => available.Any(file => StringComparer.OrdinalIgnoreCase.Equals(file.Id, id)) ? id : StoryResourceIds.Group(id)).ToArray();
            var plan = ContentPackagePlanner.Plan(available, roots, types, options.MaxPakBytes, complete, token);
            var content = ContentFiles(privateStage, gallery).ToArray();
            var contracts = ReadContractIdentity(source, catalog);
            using var buildHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            buildHash.AppendData(Encoding.UTF8.GetBytes(projectId + "\n"));
            // The same logical resources can be split differently after a default/algorithm change.
            // Keep immutable story directories distinct for those physical part sets as well.
            buildHash.AppendData("galnet-content-v3/gpak-v3/documents-v1/brotli\n"u8);
            buildHash.AppendData(JsonSerializer.SerializeToUtf8Bytes(contracts, CompactOptions));
            buildHash.AppendData(JsonSerializer.SerializeToUtf8Bytes(plan.Paks.Select(part => new
            {
                part.PackageId,
                part.PartIndex,
                part.PartCount,
                resources = part.Files.Select(file => file.Id)
            }), JsonOptions));
            var codec = new AssetMetaCodec(types);
            foreach (var file in plan.Paks.SelectMany(part => part.Files).DistinctBy(file => file.Id, StringComparer.OrdinalIgnoreCase).OrderBy(file => file.Id, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                using var input = file.OpenRead();
                buildHash.AppendData(Encoding.UTF8.GetBytes(file.Id + "\n" + CryptoHelper.HashSHA256(input) + "\n" + codec.Serialize(file.Metadata) + "\n"));
            }
            foreach (var file in content) buildHash.AppendData(Encoding.UTF8.GetBytes(file.Path + "\n" + CryptoHelper.HashSHA256(file.Data) + "\n"));
            var buildId = Convert.ToHexStringLower(buildHash.GetHashAndReset());
            var baseline = new List<Payload>();
            var stories = new List<Payload>();
            var storyEntries = new Dictionary<string, List<GalpakFileEntry>>(StringComparer.Ordinal);
            var layout = new List<object>();
            foreach (var part in plan.Paks)
            {
                token.ThrowIfCancellationRequested();
                var path = part.PackageId.Length == 0 ? $"Assets/Paks/{part.PartIndex:D3}-base.pak"
                    : $"StoryPatches/{buildId}/story-{CryptoHelper.HashSHA256(Encoding.UTF8.GetBytes(part.PackageId))[..16]}-{part.PartIndex:D3}.pak";
                var bytes = PakBuilder.Build("assets", part.Files, types, descriptor: new()
                {
                    Kind = part.PackageId.Length == 0 ? "base" : "story",
                    ProjectId = projectId,
                    BuildId = buildId,
                    PackageId = part.PackageId,
                    PartIndex = part.PartIndex,
                    PartCount = part.PartCount
                });
                var payload = Write(privateStage, path, bytes);
                (part.PackageId.Length == 0 ? baseline : stories).Add(payload);
                if (part.PackageId.Length > 0)
                {
                    if (!storyEntries.TryGetValue(part.PackageId, out var entries)) storyEntries.Add(part.PackageId, entries = []);
                    entries.Add(payload.Entry);
                }
                foreach (var file in part.Files) layout.Add(new
                {
                    id = file.Id,
                    file.Path,
                    file.TypeId,
                    package = part.PackageId,
                    pak = path,
                    dependencies = file.Metadata is IAssetDependencies dependencies ? dependencies.GetDependencies().Direct : [],
                    root = roots.Contains(file.Id, StringComparer.OrdinalIgnoreCase) || file.Id == StoryResourceIds.Graph
                });
            }
            var languageDocuments = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var file in content.Where(file => file.Path.StartsWith("I18n/", StringComparison.Ordinal)
                && !file.Path.StartsWith("I18n/catalog/", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = file.Path.Split('/');
                if (parts.Length < 3) throw new InvalidDataException("Built-in translations must be below a culture directory.");
                var culture = CultureInfo.GetCultureInfo(parts[1]).Name;
                if (culture.Length == 0) throw new InvalidDataException("Built-in translations require a named culture.");
                if (!languageDocuments.TryGetValue(culture, out var text)) languageDocuments.Add(culture, text = new(StringComparer.Ordinal));
                foreach (var pair in LocalizationJson.Texts(file.Data)) text[pair.Key] = pair.Value;
            }
            var languagePayload = Write(privateStage, PackedGameDocuments.LocalizationFileName,
                PackedGameDocuments.Build(projectId, buildId, languageDocuments.Select(pair => new KeyValuePair<string, byte[]>(
                    "texts/" + pair.Key + ".json", JsonSerializer.SerializeToUtf8Bytes(pair.Value.OrderBy(text => text.Key, StringComparer.Ordinal)
                        .ToDictionary(text => text.Key, text => text.Value, StringComparer.Ordinal))))));
            var descriptor = new GameContentManifest
            {
                Version = 3,
                ProjectId = projectId,
                BuildId = buildId,
                BasePaks = baseline.Select(file => file.Entry).ToList(),
                Stories = storyEntries.Select(pair => new StoryPackageManifest(pair.Key, pair.Value)).ToList(),
                LocalizationPak = languagePayload.Entry,
                ContractFiles = contracts
            };
            baseline.Add(languagePayload);
            var manifest = new GalpakManifest(4, projectId, projectName, DateTimeOffset.UnixEpoch, baseline.Concat(stories).Select(file => file.Entry).ToArray());
            var report = JsonSerializer.SerializeToUtf8Bytes(new { buildId, resources = layout, unused = plan.Unused.Select(file => new { file.Id, file.Path, file.TypeId }) }, JsonOptions);
            var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [GameContentManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(descriptor, CompactOptions),
                [PackedGameDocuments.InstallationPath] = JsonSerializer.SerializeToUtf8Bytes(manifest, CompactOptions),
                ["packaging-layout.json"] = report,
                ["settings.json"] = content.FirstOrDefault(file => file.Path == "settings.json").Data ?? "{}"u8.ToArray(),
                ["gallery.json"] = content.Single(file => file.Path == "gallery.json").Data,
                ["characters.json"] = content.FirstOrDefault(file => file.Path == "I18n/catalog/characters.json").Data ?? "[]"u8.ToArray()
            };
            baseline.Add(Write(privateStage, PackedGameDocuments.GameFileName, PackedGameDocuments.Build(projectId, buildId,
                documents.Select(pair => new KeyValuePair<string, byte[]>(pair.Key, CompactJson(pair.Value))))));
            return new Prepared(baseline.ToArray(), stories.ToArray(), report, buildId);
        }, ct);
        return prepared;
    }

    private static byte[] CompactJson(byte[] bytes)
    {
        using var document = JsonDocument.Parse(DecodeJson(bytes));
        return JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
    }

    private static List<GalpakFileEntry> ReadContractIdentity(string projectRoot, IEntryCatalog catalog)
    {
        var captured = ProjectSchemaContext.GetContractIdentity(catalog);
        if (captured is null) return [];
        var directory = Path.Combine(projectRoot, ProjectSchemaContext.ContractsDirectory);
        if (!captured.SequenceEqual(ProjectContractSource.ReadIdentity(directory)))
            throw new InvalidOperationException("Project contracts changed. Reopen the project before exporting.");
        return captured.ToList();
    }

    private static string DecodeJson(byte[] bytes) => LocalizationJson.Decode(bytes);

    private static Payload Write(string stage, string path, byte[] data)
    {
        var staged = ".package/" + path;
        var destination = Safe(stage, staged);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, data);
        return new(path, staged, CryptoHelper.HashSHA256(data), data.LongLength);
    }
    private static IEnumerable<(string Path, byte[] Data)> ContentFiles(string stage, GalleryCatalog gallery) =>
        Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(stage, path).Replace('\\', '/'))
            .Where(path => path == "settings.json" || path.StartsWith("I18n/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith("I18n/External/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).Select(path => (path, File.ReadAllBytes(Safe(stage, path))))
            .Append(("gallery.json", JsonSerializer.SerializeToUtf8Bytes(new GalleryConfiguration { Types = gallery.Types.ToList(), Items = gallery.Items.ToList() }, JsonOptions)));

    private static async Task VerifyDirectoryAsync(string root, IEnumerable<Payload> files, IResourceTypeCatalog types, CancellationToken ct)
    { foreach (var file in files) await VerifyFileAsync(Safe(root, file.Path), file, types, ct); }
    private static async Task VerifyFileAsync(string path, Payload file, IResourceTypeCatalog types, CancellationToken ct)
    {
        await using var input = File.OpenRead(path);
        if (input.Length != file.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct)) != file.Sha256)
            throw new InvalidDataException($"Checksum mismatch for '{file.Path}'.");
        if (file.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
            VerifyPak(file.Path, await File.ReadAllBytesAsync(path, ct), types);
    }
    private static async Task VerifyZipAsync(string path, IEnumerable<Payload> files, IResourceTypeCatalog types, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (var file in files)
        {
            var entry = zip.GetEntry("pak/" + file.Path) ?? throw new InvalidDataException($"Missing ZIP entry 'pak/{file.Path}'.");
            await using var stream = entry.Open();
            if (entry.Length != file.Size || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) != file.Sha256)
                throw new InvalidDataException($"Checksum mismatch for '{file.Path}'.");
            if (file.Path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
            {
                await using var source = entry.Open();
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, ct);
                VerifyPak(file.Path, buffer.ToArray(), types);
            }
        }
    }
    private static void VerifyPak(string path, byte[] bytes, IResourceTypeCatalog types)
    {
        if (path == PackedGameDocuments.GameFileName) _ = PackedGameDocuments.ReadGame(bytes);
        else if (path == PackedGameDocuments.LocalizationFileName) _ = PackedGameDocuments.Read(bytes);
        else { using var archive = Archive.Deserialize("assets", bytes, types); }
    }
    private static bool ExportError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException;
    private static string Safe(string root, string relative) => FileSystemPaths.ResolveRelativeFile(root, relative);
    private static string SafeName(string name) => string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    private static void Cleanup(string? directory) { if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true); }
}

public sealed record GamePackageExportResult(bool Success, string? PackagePath, string? Error)
{
    public static GamePackageExportResult Succeeded(string path) => new(true, path, null);
    public static GamePackageExportResult Failed(string error) => new(false, null, error);
}
public sealed record GameContentExportResult(bool Success, string? ContentDirectory, string? Error)
{
    public static GameContentExportResult Succeeded(string path) => new(true, path, null);
    public static GameContentExportResult Failed(string error) => new(false, null, error);
}
