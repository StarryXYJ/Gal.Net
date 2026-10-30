using System.Text.Json;
using System.Text.Json.Nodes;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

public sealed record PublishedApplicationOptions(string RuntimeIdentifier, string EntryPoint, string Version,
    string SampleFlavor = "Player", string? MediaDirectory = null);

public static partial class GameDistributionComposer
{
    /// <summary>Wraps a normal self-contained publish in the explicit lib/docs/pak application protocol.</summary>
    public static async Task PreparePublishedRuntimeAsync(string publishedDirectory, string output,
        PublishedApplicationOptions options, CancellationToken cancellationToken = default)
    {
        var linux = options.RuntimeIdentifier == "linux-x64";
        if (!linux && options.RuntimeIdentifier != "win-x64") throw new ArgumentException("Only Windows/Linux x64 publishing is supported.");
        FileSystemPaths.ValidateRelativeFile(options.EntryPoint);
        if (options.EntryPoint.Contains('/') || options.SampleFlavor is not ("Player" or "Developer") || string.IsNullOrWhiteSpace(options.Version))
            throw new ArgumentException("Specify a published apphost filename, version and Player/Developer flavor.");
        var source = Path.GetFullPath(publishedDirectory);
        var target = Path.GetFullPath(output);
        if (linux != IsTarGz(target) || (!linux && !target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Use ZIP for Windows and tar.gz for Linux.");
        foreach (var input in new[] { source, options.MediaDirectory is null ? null : Path.GetFullPath(options.MediaDirectory) })
        {
            if (input is null) continue;
            FileSystemPaths.RequireNoLinks(input, "Published inputs cannot use links.");
            if (FileSystemPaths.IsSameOrChild(target, input)) throw new InvalidDataException("Output must be outside publish/media inputs.");
        }
        FileSystemPaths.RequireNoLinks(target, "Output cannot use links.");
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Runtime output already exists.");
        foreach (var required in new[] { options.EntryPoint, linux ? "libcoreclr.so" : "coreclr.dll",
                     "GalNet.Core.dll", "GalNet.Contracts.Builtins.dll", "GalNet.Project.Contracts.dll" })
            if (!File.Exists(FileSystemPaths.ResolveRelativeFile(source, required)))
                throw new InvalidDataException($"Self-contained publish is missing '{required}'.");
        if (linux && options.MediaDirectory is null) throw new ArgumentException("Linux publishing requires a prepared private media directory.");
        if (!linux && options.MediaDirectory is not null) throw new ArgumentException("Private media-directory is currently Linux-only.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var stage = Path.Combine(Path.GetDirectoryName(target)!, ".prepare-runtime-" + Guid.NewGuid().ToString("N"));
        var temporary = target + ".prepare-" + Guid.NewGuid().ToString("N");
        try
        {
            var root = Path.Combine(stage, "application");
            await CopyTreeAsync(source, Path.Combine(root, "lib"), cancellationToken);
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            var notices = Path.Combine(root, "lib/ThirdParty");
            if (Directory.Exists(notices)) Directory.Move(notices, Path.Combine(root, "docs/ThirdParty"));
            foreach (var name in new[] { "I18n", "ResourcePatches", "StoryPatches" }) Directory.CreateDirectory(Path.Combine(root, "pak", name));
            var entry = "lib/" + options.EntryPoint;
            var modes = new Dictionary<string, UnixFileMode>(StringComparer.OrdinalIgnoreCase) { [entry] = DirectoryMode755 };
            if (linux)
            {
                var media = Path.GetFullPath(options.MediaDirectory!);
                await CopyTreeAsync(Path.Combine(media, "native"), Path.Combine(root, "lib/native"), cancellationToken);
                await CopyTreeAsync(Path.Combine(media, "licenses"), Path.Combine(root, "docs/licenses"), cancellationToken);
                FileSystemPaths.RequireNoLinks(Path.Combine(media, "media-manifest.json"), "Media provenance cannot use links.");
                File.Copy(Path.Combine(media, "media-manifest.json"), Path.Combine(root, "docs/native-media.json"));
                foreach (var required in new[] { "libvlc.so.5", "libvlccore.so.9", "vlc/plugins/codec/libavcodec_plugin.so" })
                    if (!File.Exists(Path.Combine(root, "lib/native", required))) throw new InvalidDataException($"Missing private media library '{required}'.");
                foreach (var library in Directory.EnumerateFiles(Path.Combine(root, "lib"), "*", SearchOption.AllDirectories)
                             .Where(path => Path.GetFileName(path).EndsWith(".so", StringComparison.Ordinal)
                                 || Path.GetFileName(path).Contains(".so.", StringComparison.Ordinal)))
                {
                    var header = new byte[20];
                    using var stream = File.OpenRead(library);
                    if (stream.Read(header) != 20 || !header.AsSpan(0, 6).SequenceEqual(new byte[] { 0x7f, 69, 76, 70, 2, 1 }) || header[18] != 62 || header[19] != 0)
                        throw new InvalidDataException($"Not an x64 ELF library: '{library}'.");
                }
                var launcher = "#!/bin/sh\nset -eu\napp_root=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\n"
                    + "export LD_LIBRARY_PATH=\"$app_root/lib/native:$app_root/lib/native/vlc:$app_root/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}\"\n"
                    + "export VLC_PLUGIN_PATH=\"$app_root/lib/native/vlc/plugins\"\n"
                    + "if [ -e /dev/dxg ] && [ -z \"${GALLIUM_DRIVER:-}\" ]; then export GALLIUM_DRIVER=d3d12; fi\n"
                    + "exec \"$app_root/lib/\"'" + options.EntryPoint.Replace("'", "'\"'\"'") + "' \"$@\"\n";
                await File.WriteAllTextAsync(Path.Combine(root, options.EntryPoint), launcher, cancellationToken);
                modes[options.EntryPoint] = DirectoryMode755;
            }
            var layout = new ApplicationPackageLayout
            {
                RuntimeIdentifier = options.RuntimeIdentifier, EntryPoint = linux ? options.EntryPoint : entry,
                ManagedDirectory = "lib", ContractsDirectory = "lib", ContentDirectory = "pak"
            };
            await File.WriteAllTextAsync(Path.Combine(root, "docs/README.txt"),
                $"Source-built GalNet application {options.Version}\nStart {layout.EntryPoint} from the package root. Keep the complete directory together.\n"
                + "This is a local candidate. Review native library licensing/source availability before public redistribution.\n", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, ApplicationPackageLayout.FileName), JsonSerializer.Serialize(layout, Options), cancellationToken);
            var manifest = new JsonObject
            {
                ["schemaVersion"] = 1, ["product"] = "GalNet", ["package"] = "Sample", ["version"] = options.Version,
                ["runtime"] = options.RuntimeIdentifier, ["selfContained"] = true, ["readyToRun"] = false,
                ["sampleFlavor"] = options.SampleFlavor, ["nativeEntryPoint"] = entry,
                ["files"] = JsonSerializer.SerializeToNode(await CaptureFilesAsync(root, cancellationToken), Options)
            };
            await File.WriteAllTextAsync(Path.Combine(root, ManifestPath), manifest.ToJsonString(Options), cancellationToken);
            await VerifyApplicationAsync(root, ApplicationPackageLayout.Read(root), cancellationToken);
            if (linux)
            {
                VerifyLinuxEntry(root, layout, manifest, modes);
                await WriteTarAsync(root, temporary, "runtime", modes, cancellationToken);
            }
            else await WriteZipAsync(root, temporary, cancellationToken);
            await FilePublication.MoveFileAsync(temporary, target, cancellationToken: cancellationToken);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
