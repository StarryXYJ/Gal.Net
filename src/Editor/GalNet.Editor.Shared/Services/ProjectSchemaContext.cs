using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using GalNet.Contracts.Builtins;
using GalNet.Core.Configuration;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;

namespace GalNet.Editor.Shared.Services;

/// <summary>Owns one project's schema catalog and collectible assembly context.</summary>
public sealed class ProjectSchemaContext : IDisposable
{
    private static readonly ConditionalWeakTable<IEntryCatalog, List<GalpakFileEntry>> CatalogIdentities = new();
    internal static IReadOnlyList<GalpakFileEntry>? GetContractIdentity(IEntryCatalog catalog) =>
        CatalogIdentities.TryGetValue(catalog, out var identity) ? identity : null;
    public const string ContractsDirectory = "Contracts";
    public const string AssemblyFileName = "GalNet.Project.Contracts.dll";
    public const string EntryPointTypeName = "GalNet.Project.Contracts.ProjectSchemas";
    public const string ModulesPropertyName = "Modules";

    private SchemaLoadContext? _loadContext;
    private IEntryCatalog? _catalog;

    private ProjectSchemaContext(string assemblyPath, string fingerprint, SchemaLoadContext context, IEntryCatalog catalog, ProjectConfigurationSchema configurations)
    {
        AssemblyPath = assemblyPath;
        Fingerprint = fingerprint;
        _loadContext = context;
        _catalog = catalog;
        Configurations = configurations;
    }

    public ProjectConfigurationSchema Configurations { get; private set; }
    public string AssemblyPath { get; }
    public string Fingerprint { get; }
    public IEntryCatalog Catalog => _catalog ?? throw new ObjectDisposedException(nameof(ProjectSchemaContext));

    /// <summary>Loads the declared project contract; never substitutes built-in schemas.</summary>
    public static ProjectSchemaContext Load(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        return LoadContractsDirectory(Path.Combine(projectDirectory, ContractsDirectory));
    }

    /// <summary>Validates a built contract directory before deployment or project creation.</summary>
    public static ProjectSchemaContext LoadContractsDirectory(string contractsDirectory)
    {
        var path = Path.GetFullPath(Path.Combine(contractsDirectory, AssemblyFileName));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Project entry contract is missing: '{path}'. Build and deploy the project's Contracts project.", path);
        path = new ProjectRawReader(contractsDirectory).Resolve(AssemblyFileName);

        var context = new SchemaLoadContext(path);
        try
        {
            var identity = ProjectContractSource.ReadIdentity(contractsDirectory);
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, writable: false);
            var assembly = context.LoadFromStream(stream);
            var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(assembly.GetName().Name, Path.GetFileNameWithoutExtension(AssemblyFileName), StringComparison.Ordinal))
                throw new InvalidDataException($"Contract assembly must be named '{Path.GetFileNameWithoutExtension(AssemblyFileName)}'.");
            var type = assembly.GetType(EntryPointTypeName, throwOnError: true)!;
            if (!type.IsVisible) throw new InvalidDataException($"Contract entry point '{EntryPointTypeName}' must be public.");
            var property = type.GetProperty(ModulesPropertyName, BindingFlags.Public | BindingFlags.Static);
            if (property?.GetMethod is not { IsPublic: true, IsStatic: true } ||
                property.GetIndexParameters().Length != 0 ||
                !typeof(IReadOnlyList<IEntryModule>).IsAssignableFrom(property.PropertyType))
                throw new InvalidDataException($"Expected public static IReadOnlyList<IEntryModule> {EntryPointTypeName}.{ModulesPropertyName} {{ get; }}.");
            if (property.GetValue(null) is not IReadOnlyList<IEntryModule> modules)
                throw new InvalidDataException($"{EntryPointTypeName}.{ModulesPropertyName} returned null.");
            foreach (var module in modules)
            {
                if (module is null || string.IsNullOrWhiteSpace(module.Id))
                    throw new InvalidDataException($"Null or empty schema module ID '{module?.Id}'.");
            }
            var configurationProperty = type.GetProperty("Configurations", BindingFlags.Public | BindingFlags.Static);
            var configurations = ProjectConfigurationSchema.Empty;
            if (configurationProperty is not null)
            {
                if (configurationProperty.PropertyType != typeof(ProjectConfigurationSchema) ||
                    configurationProperty.GetMethod is not { IsPublic: true, IsStatic: true } || configurationProperty.GetIndexParameters().Length != 0 ||
                    configurationProperty.GetValue(null) is not ProjectConfigurationSchema declared)
                    throw new InvalidDataException("ProjectSchemas.Configurations must return a nonnull ProjectConfigurationSchema.");
                configurations = declared;
            }
            if (!identity.SequenceEqual(ProjectContractSource.ReadIdentity(contractsDirectory)))
                throw new IOException("Project contracts changed while their schemas were being loaded.");
            var catalog = new TargetProfileEntryCatalog(modules);
            CatalogIdentities.Add(catalog, identity);
            return new ProjectSchemaContext(path, fingerprint, context, catalog, configurations);
        }
        catch (Exception exception)
        {
            context.Unload();
            throw new InvalidDataException($"Cannot load project entry contract '{path}': {exception.GetBaseException().Message}", exception);
        }
    }

    public void Dispose()
    {
        _catalog = null;
        Configurations = ProjectConfigurationSchema.Empty;
        var context = Interlocked.Exchange(ref _loadContext, null);
        context?.Unload();
    }

    private sealed class SchemaLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _directory;
        private static readonly Dictionary<string, Assembly> SharedAssemblies = new(StringComparer.Ordinal)
        {
            [typeof(IEntryModule).Assembly.GetName().Name!] = typeof(IEntryModule).Assembly,
            [typeof(BuiltinSchemas).Assembly.GetName().Name!] = typeof(BuiltinSchemas).Assembly
        };

        public SchemaLoadContext(string path) : base($"ProjectSchemas:{path}:{Guid.NewGuid()}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(path);
            _directory = Path.GetDirectoryName(path)!;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (SharedAssemblies.TryGetValue(assemblyName.Name!, out var shared))
            {
                if (!string.Equals(assemblyName.FullName, shared.GetName().FullName, StringComparison.Ordinal))
                    throw new FileLoadException($"Incompatible shared contract '{assemblyName.FullName}'; tool provides '{shared.GetName().FullName}'.");
                var source = Path.Combine(_directory, assemblyName.Name + ".dll");
                if (!SHA256.HashData(File.ReadAllBytes(source)).AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(shared.Location))))
                    throw new FileLoadException($"Incompatible shared contract bytes: '{assemblyName.Name}'. Rebuild and deploy matching contracts.");
                return shared;
            }
            // A self-contained application directory also contains framework DLLs.
            // Loading them into this context would split JsonElement/Action type identity.
            if (ProjectContractSource.IsPlatformAssembly(assemblyName.Name!)) return null;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null)
            {
                var adjacent = Path.Combine(_directory, assemblyName.Name + ".dll");
                if (File.Exists(adjacent)) path = adjacent;
            }
            if (path is not null) return LoadManagedAssembly(path);
            throw new FileNotFoundException($"Contract dependency '{assemblyName.FullName}' is missing from '{_directory}'.");
        }

        // Stream loading avoids locking project files until a nondeterministic GC unload.
        public Assembly LoadManagedAssembly(string path)
        {
            path = new ProjectRawReader(_directory).Resolve(Path.GetRelativePath(_directory, path));
            using var stream = File.OpenRead(path);
            return LoadFromStream(stream);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName) =>
            throw new InvalidDataException($"Schema contract attempted to load native dependency '{unmanagedDllName}'. Keep platform execution outside Contracts.");
    }
}
