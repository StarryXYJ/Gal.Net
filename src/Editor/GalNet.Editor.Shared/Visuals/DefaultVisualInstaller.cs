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
            if (!catalog.TryGet(BuiltinEntryIds.ApplyEffect, out _) || !configurations.Sections.Any(section => section.Id == DefaultProjectConfiguration.Section.Id && section.Fields.Any(field => field.Id == "defaultVisualPrograms")))
                throw new InvalidDataException("This project contract does not declare default visual bindings and effect.apply. Rebuild/deploy its updated contract first.");
            var reader = new ProjectRawReader(root);
            var originalSettings = await File.ReadAllBytesAsync(reader.Resolve("settings.json"), cancellationToken);
            var configuration = JsonSerializer.Deserialize<ProjectConfiguration>(originalSettings, JsonOptions) ?? throw new InvalidDataException("Invalid project settings.");
            configurations.Validate(configuration);
            var plan = DefaultVisualTemplate.Prepare(configuration);
            var report = new DefaultVisualInstallReport(dryRun, plan.Files.Keys.Order(StringComparer.Ordinal).ToArray(), plan.Configuration.ReadDefaultOptions().DefaultVisualPrograms);
            foreach (var path in plan.Files.Keys)
                if (File.Exists(reader.Resolve(path)) || Directory.Exists(reader.Resolve(path)))
                    throw new IOException($"Default visual path is occupied; preserve it and configure its binding explicitly: '{path}'.");
            if (plan.Files.Count == 0) return new(operation, true, report, []);
            scratch = Path.Combine(Path.GetTempPath(), "GalNet-visual-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            var writes = new List<ProjectApplyWrite>();
            var payloads = plan.Files.Concat(new[] { new KeyValuePair<string, byte[]>("settings.json", JsonSerializer.SerializeToUtf8Bytes(plan.Configuration, JsonOptions)) });
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
