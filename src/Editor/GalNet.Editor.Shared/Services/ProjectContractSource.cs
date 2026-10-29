using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using GalNet.Core.Serialization;
using GalNet.Editor.Abstraction.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.Editor.Shared.Services;

/// <summary>Snapshots only a contract's managed dependency closure for project creation.</summary>
public sealed class ProjectContractSource : IDisposable
{
    private static readonly HashSet<string> PlatformAssemblyNames = GetPlatformAssemblyNames();
    internal static bool IsPlatformAssembly(string name) => PlatformAssemblyNames.Contains(name);

    private static HashSet<string> GetPlatformAssemblyNames()
    {
        // In self-contained deployments the runtime and app assemblies share a directory.
        // The runtime pack's deps entry identifies framework files without excluding app dependencies.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depsFiles = ((string?)AppContext.GetData("APP_CONTEXT_DEPS_FILES") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in depsFiles)
        {
            using var deps = JsonDocument.Parse(File.ReadAllBytes(path));
            foreach (var target in deps.RootElement.GetProperty("targets").EnumerateObject())
                foreach (var library in target.Value.EnumerateObject())
                    if (library.Name.StartsWith("runtimepack.Microsoft.NETCore.App.Runtime.", StringComparison.Ordinal)
                        && library.Value.TryGetProperty("runtime", out var runtime))
                        foreach (var asset in runtime.EnumerateObject())
                            if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                names.Add(Path.GetFileNameWithoutExtension(asset.Name));
        }
        if (names.Count != 0) return names;

        return ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => Path.GetDirectoryName(path) == Path.GetDirectoryName(typeof(object).Assembly.Location))
            .Select(path => Path.GetFileNameWithoutExtension(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private ProjectContractSource(string directoryPath) => DirectoryPath = directoryPath;
    public string DirectoryPath { get; }

    public static string RequireApplication(string executable)
    {
        var path = Path.GetFullPath(executable.Trim());
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new EditorOperationException("project.applicationInvalid", "Select an existing game application EXE.");
        return path;
    }

    public static string FindForApplication(string executable)
    {
        var root = Path.GetDirectoryName(RequireApplication(executable))!;
        if (File.Exists(Path.Combine(root, ApplicationPackageLayout.FileName)))
        {
            var layout = ApplicationPackageLayout.Read(root);
            if (!string.Equals(Path.GetFullPath(Path.Combine(root, layout.EntryPoint)), Path.GetFullPath(executable),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new EditorOperationException("project.applicationInvalid", "The selected application does not match its package layout.");
            var contracts = Path.Combine(root, layout.ContractsDirectory);
            if (!File.Exists(Path.Combine(contracts, ProjectSchemaContext.AssemblyFileName)))
                throw new EditorOperationException("project.applicationContractMissing", "The application layout declares a missing project contract.");
            return contracts;
        }
        var candidates = new[] { root, Path.Combine(root, "lib") }
            .Where(directory => File.Exists(Path.Combine(directory, ProjectSchemaContext.AssemblyFileName))).ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new EditorOperationException("project.applicationContractMissing", "No project contract was found beside the application or in lib. Select a contract directory manually."),
            _ => throw new EditorOperationException("project.applicationContractAmbiguous", "Multiple project contracts were found. Select a contract directory manually.")
        };
    }

    public static ProjectContractSource Prepare(string sourceDirectory)
    {
        var files = ReadClosure(sourceDirectory);
        var snapshot = new ProjectContractSource(Path.Combine(Path.GetTempPath(), "GalNet-contracts-" + Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(snapshot.DirectoryPath);
            foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(snapshot.DirectoryPath, name), bytes);
            using var validated = ProjectSchemaContext.LoadContractsDirectory(snapshot.DirectoryPath);
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    internal static List<GalpakFileEntry> ReadIdentity(string sourceDirectory) => ReadClosure(sourceDirectory)
        .OrderBy(file => file.Key, StringComparer.Ordinal)
        .Select(file => new GalpakFileEntry(file.Key, Convert.ToHexStringLower(SHA256.HashData(file.Value)), file.Value.LongLength)).ToList();

    private static Dictionary<string, byte[]> ReadClosure(string sourceDirectory)
    {
        var reader = new ProjectRawReader(sourceDirectory);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        ReadAssembly(ProjectSchemaContext.AssemblyFileName);
        return files;

        void ReadAssembly(string fileName)
        {
            if (fileName != Path.GetFileName(fileName))
                throw new InvalidDataException($"Invalid contract dependency name '{fileName}'.");
            if (files.ContainsKey(fileName)) return;
            var bytes = File.ReadAllBytes(reader.Resolve(fileName));
            files.Add(fileName, bytes);
            using var stream = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                if (!PlatformAssemblyNames.Contains(name)) ReadAssembly(name + ".dll");
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}
