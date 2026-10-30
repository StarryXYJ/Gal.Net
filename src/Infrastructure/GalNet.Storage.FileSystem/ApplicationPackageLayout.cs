using System.Text.Json;
using GalNet.Assets;

namespace GalNet.Storage.FileSystem;

/// <summary>Paths relative to the distribution root; normal development output does not require a layout.</summary>
public sealed record ApplicationPackageLayout
{
    public const string FileName = "docs/application-layout.json";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly StorageJsonContext Context = new(Options);

    public int SchemaVersion { get; init; } = 1;
    public required string RuntimeIdentifier { get; init; }
    public required string EntryPoint { get; init; }
    public required string ManagedDirectory { get; init; }
    public required string ContractsDirectory { get; init; }
    public required string ContentDirectory { get; init; }

    public static ApplicationPackageLayout Read(string root)
    {
        var path = FileSystemPaths.ResolveRelativeFile(root, FileName);
        FileSystemPaths.RequireNoLinks(path, "Application layout cannot use symbolic links.");
        var layout = JsonSerializer.Deserialize(File.ReadAllBytes(path), Context.ApplicationPackageLayout)
            ?? throw new InvalidDataException("Missing application layout.");
        if (layout.SchemaVersion != 1 || layout.RuntimeIdentifier is not ("win-x64" or "linux-x64" or "osx-x64" or "osx-arm64"))
            throw new InvalidDataException("Unsupported application layout version or runtime identifier.");
        foreach (var relative in new[] { layout.EntryPoint, layout.ManagedDirectory, layout.ContractsDirectory, layout.ContentDirectory })
            FileSystemPaths.ValidateRelativeFile(relative);
        var content = FileSystemPaths.ResolveRelativeFile(root, layout.ContentDirectory);
        foreach (var relative in new[] { layout.EntryPoint, layout.ManagedDirectory, layout.ContractsDirectory, FileName, "docs/release-manifest.json" })
        {
            var applicationPath = FileSystemPaths.ResolveRelativeFile(root, relative);
            if (FileSystemPaths.IsSameOrChild(applicationPath, content) || FileSystemPaths.IsSameOrChild(content, applicationPath))
                throw new InvalidDataException("Content and application paths must not overlap.");
        }
        return layout;
    }

    public static string FindApplicationRoot(string assemblyDirectory)
    {
        var assemblyRoot = Path.GetFullPath(assemblyDirectory);
        for (var current = new DirectoryInfo(assemblyRoot); current is not null; current = current.Parent)
        {
            if (!File.Exists(Path.Combine(current.FullName, FileName))) continue;
            var layout = Read(current.FullName);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(current.FullName, assemblyRoot.TrimEnd(Path.DirectorySeparatorChar), comparison)
                && !string.Equals(FileSystemPaths.ResolveRelativeFile(current.FullName, layout.ManagedDirectory), assemblyRoot.TrimEnd(Path.DirectorySeparatorChar), comparison))
                throw new InvalidDataException("Application layout does not match the managed entry directory.");
            return current.FullName;
        }
        return assemblyRoot;
    }

    public static string GetContentDirectory(string applicationRoot) => File.Exists(Path.Combine(applicationRoot, FileName))
        ? FileSystemPaths.ResolveRelativeFile(applicationRoot, Read(applicationRoot).ContentDirectory)
        : Path.Combine(applicationRoot, "pak");
}
