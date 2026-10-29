using System.Runtime.Loader;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class ProjectSchemaContextTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-schema-tests-" + Guid.NewGuid())).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void SameAssemblyName_HasIndependentCatalogs_AndSharesBuiltinTypes()
    {
        var a = Deploy("SchemaA");
        var b = Deploy("SchemaB");
        using var first = ProjectSchemaContext.Load(a);
        using var second = ProjectSchemaContext.Load(b);
        Assert.Multiple(() =>
        {
            Assert.That(first.Catalog.TryGet("custom.example", out _), Is.True);
            Assert.That(second.Catalog.TryGet("custom.example", out _), Is.False);
            Assert.That(first.Catalog.Definitions, Has.Count.EqualTo(5));
            Assert.That(second.Catalog.Definitions, Has.Count.EqualTo(2));
            Assert.That(second.Catalog.TryGet("custom.marker", out _), Is.True);
            Assert.That(first.Fingerprint, Is.Not.EqualTo(second.Fingerprint));
            Assert.That(first.Catalog.Definitions.Where(definition => definition.Kind == EntryKind.Primitive)
                .All(definition => definition.Factory().GetType().Assembly == typeof(Entry).Assembly), Is.True);
            Assert.That(AssemblyLoadContext.All.Where(context => context.Name?.StartsWith("ProjectSchemas:", StringComparison.Ordinal) == true)
                .SelectMany(context => context.Assemblies).Any(assembly => assembly.GetName().Name == "GalNet.Contracts.Builtins"), Is.False);
            Assert.That(AssemblyLoadContext.Default.Assemblies.Any(assembly => assembly.GetName().Name == "GalNet.Project.Contracts"), Is.False);
        });
        var composite = (CompositeEntry)first.Catalog.Create("custom.bundle");
        var compiled = composite.Compile(new EntryCompileContext { SourceEntryId = "source", Condition = "true" });
        Assert.That(compiled.Single().Type, Is.EqualTo("custom.example"));
    }

    [Test]
    public void MissingContract_IsExplicit_WithoutBuiltinFallback()
    {
        var error = Assert.Throws<FileNotFoundException>(() => ProjectSchemaContext.Load(_directory));
        Assert.That(error!.Message, Does.Contain("Contracts").And.Contain("GalNet.Project.Contracts.dll"));
    }

    [Test]
    public void MissingDependency_ReportsDependencyAndContractPath()
    {
        var project = Deploy("SchemaA");
        File.Delete(Path.Combine(project, "Contracts", "SchemaDependency.dll"));
        var error = Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(project));
        Assert.That(error!.Message, Does.Contain("SchemaDependency").And.Contain(project));
    }

    [Test]
    public void DuplicateEntryId_IsRejected()
    {
        var project = Deploy("SchemaDuplicate");
        var error = Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(project));
        Assert.That(error!.Message, Does.Contain("already present"));
    }

    [Test]
    public void WrongEntryPointShape_IsRejectedWithExpectedSignature()
    {
        var project = Deploy("SchemaInvalid");
        var error = Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(project));
        Assert.That(error!.Message, Does.Contain("IReadOnlyList<IEntryModule>").And.Contain("ProjectSchemas.Modules"));
    }

    [Test]
    public void SameVersionCoreWithDifferentBytesIsRejected()
    {
        var project = Deploy("SchemaA");
        var path = Path.Combine(project, "Contracts/GalNet.Core.dll");
        var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        var error = Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(project));
        Assert.That(error!.Message, Does.Contain("Incompatible shared contract bytes"));
    }

    [Test]
    public void IncompatibleCoreVersion_IsRejectedWithoutLoadingDuplicateCore()
    {
        var project = Deploy("SchemaIncompatible");
        var error = Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(project));
        Assert.That(error!.Message, Does.Contain("Incompatible shared contract").And.Contain("99.0.0.0"));
    }

    [Test]
    public void InvalidAssembly_IsRejectedWithFileLocation()
    {
        var project = Deploy("SchemaB");
        var dll = Path.Combine(project, "Contracts", ProjectSchemaContext.AssemblyFileName);
        File.WriteAllText(dll, "not an assembly");
        var error = Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(project));
        Assert.That(error!.Message, Does.Contain(dll));
    }

    [Test]
    public void Dispose_InvalidatesCatalog_AndProjectCanBeReopened()
    {
        var project = Deploy("SchemaB");
        var context = ProjectSchemaContext.Load(project);
        context.Dispose();
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = context.Catalog);
        using var reopened = ProjectSchemaContext.Load(project);
        Assert.That(reopened.Catalog.Definitions, Has.Count.EqualTo(2));
    }

    [Test]
    public void LoadedFiles_CanBeReplacedWithoutWaitingForGarbageCollection()
    {
        var project = Deploy("SchemaB");
        using var original = ProjectSchemaContext.Load(project);
        var replacement = Deploy("SchemaA");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(replacement, "Contracts")))
            File.Copy(file, Path.Combine(project, "Contracts", Path.GetFileName(file)), overwrite: true);
        using var reopened = ProjectSchemaContext.Load(project);
        Assert.That(original.Catalog.TryGet("custom.bundle", out _), Is.False);
        Assert.That(reopened.Catalog.TryGet("custom.bundle", out _), Is.True);
    }

    [Test]
    public void SchemaDescriptions_UseTheProjectCollectionAndOrigin()
    {
        using var context = ProjectSchemaContext.Load(Deploy("SchemaA"));
        var result = ProjectSchemaQueryService.Describe(context, kind: "composite");
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Data);
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("contract").GetProperty("sha256").GetString(), Is.EqualTo(context.Fingerprint));
            Assert.That(json.GetProperty("result").GetProperty("Items")[0].GetProperty("TypeId").GetString(), Is.EqualTo("custom.bundle"));
            Assert.That(json.GetProperty("result").GetProperty("Total").GetInt32(), Is.EqualTo(1));
            Assert.That(ProjectSchemaQueryService.Describe(context, "not.declared").Success, Is.False);
        });
    }

    [Test]
    public async Task CliSchemaQuery_LoadsCustomProjectContract()
    {
        var project = Deploy("SchemaA");
        var (exit, output, error) = await GeneralTest.Editor.Shared.CliHelpTests.Invoke(["schema", "show", project, "custom.bundle"]);
        using var parsed = System.Text.Json.JsonDocument.Parse(output);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, error);
            Assert.That(parsed.RootElement.GetProperty("data").GetProperty("result").GetProperty("items")[0].GetProperty("typeId").GetString(), Is.EqualTo("custom.bundle"));
            Assert.That(parsed.RootElement.GetProperty("data").GetProperty("contract").GetProperty("sha256").GetString(), Has.Length.EqualTo(64));
        });
    }

    [Test]
    public void AncestorLinks_AreRejectedForRawReadsAndContractLoads()
    {
        var project = Deploy("SchemaA");
        var link = Path.Combine(_directory, "link");
        DirectoryLinks.Create(link, _directory);
        try
        {
            var linkedProject = Path.Combine(link, Path.GetFileName(project));
            Assert.Throws<InvalidDataException>(() => new ProjectRawReader(linkedProject));
            Assert.Throws<InvalidDataException>(() => ProjectSchemaContext.Load(linkedProject));
        }
        finally { Directory.Delete(link); }
    }

    private string Deploy(string fixture)
    {
        var root = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var source = Path.Combine(root, "test", "Fixtures", fixture, "bin", configuration, "net10.0");
        var project = Path.Combine(_directory, fixture);
        var destination = Directory.CreateDirectory(Path.Combine(project, "Contracts")).FullName;
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        return project;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
