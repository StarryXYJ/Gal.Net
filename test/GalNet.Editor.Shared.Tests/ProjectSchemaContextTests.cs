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
            Assert.That(second.Catalog.Definitions, Has.Count.EqualTo(1));
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
        Assert.That(reopened.Catalog.Definitions, Has.Count.EqualTo(1));
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
