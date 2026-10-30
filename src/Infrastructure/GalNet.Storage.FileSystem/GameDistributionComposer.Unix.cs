using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

public static partial class GameDistributionComposer
{
    private static bool IsTarGz(string path) => path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    private const UnixFileMode FileMode644 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode DirectoryMode755 = FileMode644 | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private static UnixFileMode SafeMode(UnixFileMode mode)
    {
        if (((int)mode & ~0x1ff) != 0) throw new InvalidDataException("Privileged Unix permissions are not supported in runtime packages.");
        return mode;
    }

    private static Dictionary<string, UnixFileMode> CaptureUnixModes(string root)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Unix permissions require a Unix filesystem.");
        var result = new Dictionary<string, UnixFileMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in Files(root))
            result.Add(relative, SafeMode(File.GetUnixFileMode(FileSystemPaths.ResolveRelativeFile(root, relative))));
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            result.Add(Path.GetRelativePath(root, directory).Replace('\\', '/'), SafeMode(File.GetUnixFileMode(directory)));
        return result;
    }

    private static async Task<Dictionary<string, UnixFileMode>> ExtractTarRuntimeAsync(string source, string target, CancellationToken ct)
    {
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(target);
        await using var input = File.OpenRead(source);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken: ct) is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.Name.TrimEnd('/');
            var destination = FileSystemPaths.ResolveRelativeFile(target, relative);
            if (!modes.TryAdd(relative, SafeMode(entry.Mode))
                || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                throw new InvalidDataException("Runtime tar contains duplicate paths, links or unsupported entry types.");
            if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, ct);
        }
        return modes;
    }

    private static void VerifyLinuxEntry(string root, ApplicationPackageLayout layout, JsonObject manifest,
        IReadOnlyDictionary<string, UnixFileMode> modes)
    {
        var native = manifest["nativeEntryPoint"]?.GetValue<string>() ?? layout.EntryPoint;
        var path = FileSystemPaths.ResolveRelativeFile(root, native);
        foreach (var entry in new[] { layout.EntryPoint, native })
            if (!modes.TryGetValue(entry, out var mode) || (mode & UnixFileMode.UserExecute) == 0)
                throw new InvalidDataException($"Linux entry is not executable: '{entry}'.");
        Span<byte> header = stackalloc byte[20];
        using (var stream = File.OpenRead(path))
            if (stream.Read(header) != header.Length || !header[..6].SequenceEqual(new byte[] { 0x7f, 69, 76, 70, 2, 1 })
                || header[18] != 62 || header[19] != 0)
                throw new InvalidDataException("The linux-x64 native entry must be an x64 little-endian ELF apphost.");
        if (native != layout.EntryPoint)
        {
            using var stream = File.OpenRead(FileSystemPaths.ResolveRelativeFile(root, layout.EntryPoint));
            Span<byte> shebang = stackalloc byte[10];
            if (stream.Read(shebang) != shebang.Length || !shebang.SequenceEqual("#!/bin/sh\n"u8))
                throw new InvalidDataException("The Linux launcher must be a /bin/sh script.");
        }
    }

    private static async Task WriteTarAsync(string root, string target, string archiveRoot,
        IReadOnlyDictionary<string, UnixFileMode> modes, CancellationToken ct)
    {
        FileSystemPaths.ValidateRelativeFile(archiveRoot);
        await using var output = File.Create(target);
        await using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        await using var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, archiveRoot)
            { Mode = DirectoryMode755, ModificationTime = DateTimeOffset.UnixEpoch }, ct);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.Directory, archiveRoot + "/" + relative)
                { Mode = modes.GetValueOrDefault(relative, DirectoryMode755), ModificationTime = DateTimeOffset.UnixEpoch }, ct);
        }
        foreach (var relative in Files(root).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            await using var input = File.OpenRead(FileSystemPaths.ResolveRelativeFile(root, relative));
            var entry = new PaxTarEntry(TarEntryType.RegularFile, archiveRoot + "/" + relative)
            {
                Mode = modes.GetValueOrDefault(relative, FileMode644), ModificationTime = DateTimeOffset.UnixEpoch,
                DataStream = input
            };
            await writer.WriteEntryAsync(entry, ct);
        }
    }
}
