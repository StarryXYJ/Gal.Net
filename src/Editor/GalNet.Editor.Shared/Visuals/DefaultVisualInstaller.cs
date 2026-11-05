using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Visuals;

public sealed record DefaultVisualInstallReport(bool DryRun, IReadOnlyList<string> AddedFiles, IReadOnlyDictionary<string, string> Programs);

/// <summary>Explicit additive installation using the existing checked author-file transaction.</summary>
public static class DefaultVisualInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<ToolResult> InstallAsync(string root, IEntryCatalog catalog, ProjectConfigurationSchema configurations,
        bool dryRun = false, CancellationToken cancellationToken = default)
    {
        const string operation = "project.visuals.install";
        string? scratch = null;
        try
        {
            if (!catalog.TryGet(BuiltinEntryIds.ApplyEffect, out _) || !configurations.Sections.Any(section => section.Id == DefaultProjectConfiguration.Section.Id))
                throw new InvalidDataException("This project contract does not declare the default project section and effect.apply.");
            var reader = new ProjectRawReader(root);
            var originalSettings = await File.ReadAllBytesAsync(reader.Resolve("settings.json"), cancellationToken);
            var configuration = JsonSerializer.Deserialize<ProjectConfiguration>(originalSettings, JsonOptions) ?? throw new InvalidDataException("Invalid project settings.");
            configurations.Validate(configuration);
            var plan = DefaultVisualTemplate.Prepare(configuration);
            var additions = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (path, bytes) in plan.Files.Where(item => !item.Key.EndsWith(".meta", StringComparison.Ordinal)))
            {
                var resource = reader.Resolve(path);
                var metadata = reader.Resolve(path + ".meta");
                var resourceExists = File.Exists(resource);
                var metadataExists = File.Exists(metadata);
                if (Directory.Exists(resource) || Directory.Exists(metadata) || resourceExists != metadataExists)
                    throw new IOException($"Default visual path is occupied or incomplete: '{path}'.");
                if (!resourceExists)
                {
                    additions.Add(path, bytes);
                    additions.Add(path + ".meta", plan.Files[path + ".meta"]);
                    continue;
                }
                using var expected = JsonDocument.Parse(plan.Files[path + ".meta"]);
                using var actual = JsonDocument.Parse(await File.ReadAllBytesAsync(metadata, cancellationToken));
                if (actual.RootElement.ValueKind != JsonValueKind.Object ||
                    !actual.RootElement.TryGetProperty("id", out var actualId) || actualId.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(actualId.GetString(), out var actualGuid) ||
                    actualGuid != Guid.Parse(expected.RootElement.GetProperty("id").GetString()!) ||
                    !actual.RootElement.TryGetProperty("type", out var actualType) || actualType.ValueKind != JsonValueKind.String ||
                    actualType.GetString() != expected.RootElement.GetProperty("type").GetString())
                    throw new InvalidDataException($"Default visual identity or type differs: '{path}'.");
            }
            var report = new DefaultVisualInstallReport(dryRun, additions.Keys.Order(StringComparer.Ordinal).ToArray(), BuiltinVisualResources.Programs);
            if (additions.Count == 0) return new(operation, true, report, []);
            scratch = Path.Combine(Path.GetTempPath(), "GalNet-visual-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            var writes = new List<ProjectApplyWrite>();
            var payloads = additions;
            foreach (var (path, bytes) in payloads)
            {
                var source = writes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".data";
                await File.WriteAllBytesAsync(Path.Combine(scratch, source), bytes, cancellationToken);
                writes.Add(new()
                {
                    Path = path,
                    Source = source,
                    ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
                    ExpectedHash = path == "settings.json" ? Convert.ToHexString(SHA256.HashData(originalSettings)) : null
                });
            }
            var manifest = Path.Combine(scratch, "manifest.json");
            await File.WriteAllBytesAsync(manifest, JsonSerializer.SerializeToUtf8Bytes(new ProjectApplyManifest { Version = 1, Writes = writes, Deletes = [] }, JsonOptions), cancellationToken);
            var result = await new ProjectApplyService(root, catalog, configurations: configurations).ApplyAsync(manifest, dryRun, cancellationToken);
            return result with { OperationId = operation, Data = result.Success ? report : result.Data };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        { return new(operation, false, null, [new("visual.installRejected", "error", error.Message)]); }
        finally { if (scratch is not null && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }
}
