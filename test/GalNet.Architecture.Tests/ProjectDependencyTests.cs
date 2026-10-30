using System.Xml.Linq;

namespace GeneralTest.Architecture;

public class ProjectDependencyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly IReadOnlyDictionary<string, string[]> AllowedDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GalNet.Core"] = [],
            ["GalNet.Diagnostics"] = [],
            ["GalNet.Contracts.Builtins"] = ["GalNet.Core", "GalNet.SchemaGenerator"],
            ["GalNet.Sample.Contracts"] = ["GalNet.Core", "GalNet.Contracts.Builtins", "GalNet.SchemaGenerator"],
            ["GalNet.SchemaGenerator"] = [],
            ["GalNet.Presentation.Abstractions"] = ["GalNet.Contracts.Builtins", "GalNet.Core"],
            ["GalNet.GameHost"] = ["GalNet.Contracts.Builtins", "GalNet.Core", "GalNet.Runtime", "GalNet.Presentation.Abstractions"],
            ["GalNet.Runtime"] = ["GalNet.Core"],
            ["GalNet.Debugging"] = ["GalNet.Runtime"],
            ["GalNet.Primitives.Builtins"] = ["GalNet.Core", "GalNet.Contracts.Builtins", "GalNet.Presentation.Abstractions", "GalNet.Runtime"],
            ["GalNet.Media.Audio"] = ["GalNet.Contracts.Builtins", "GalNet.Core"],
            ["GalNet.Input.Sdl3"] = ["GalNet.Presentation.Abstractions"],
            ["GalNet.Media.Miniaudio"] = ["GalNet.Contracts.Builtins", "GalNet.Core", "GalNet.Media.Audio"],
            ["GalNet.Media.LibVlc"] = ["GalNet.Contracts.Builtins", "GalNet.Core", "GalNet.Assets", "GalNet.Presentation.Abstractions"],
            ["GalNet.Assets"] = ["GalNet.Contracts.Builtins", "GalNet.Core"],
            ["GalNet.Storage.FileSystem"] = ["GalNet.Contracts.Builtins", "GalNet.Core", "GalNet.Runtime", "GalNet.Assets", "GalNet.GameHost"],
            ["GalNet.Editor.Abstraction"] = ["GalNet.Core"],
            ["GalNet.Editor.DefaultContracts"] = ["GalNet.Contracts.Builtins"],
            ["GalNet.Editor.Shared"] =
            [
                "GalNet.Debugging",
                "GalNet.Core",
                "GalNet.Editor.Abstraction",
                "GalNet.Contracts.Builtins",
                "GalNet.Runtime",
                "GalNet.Assets",
                "GalNet.Storage.FileSystem"
            ],
            ["GalNet.Avalonia.GameHost"] = ["GalNet.Contracts.Builtins", "GalNet.Storage.FileSystem", "GalNet.Media.LibVlc", "GalNet.Media.Miniaudio", "GalNet.Input.Sdl3", "GalNet.GameHost", "GalNet.Avalonia.Controls", "GalNet.Avalonia.GameView", "GalNet.Avalonia.Rendering", "GalNet.Primitives.Builtins", "GalNet.Runtime", "GalNet.Presentation.Abstractions", "GalNet.Assets", "GalNet.Core"],
            ["GalNet.Avalonia.Controls"] = ["GalNet.Contracts.Builtins", "GalNet.Core"],
            ["GalNet.Avalonia.Themes"] = ["GalNet.Contracts.Builtins", "GalNet.Avalonia.Controls", "GalNet.Avalonia.GameView"],
            ["GalNet.Avalonia.GameView"] =
            ["GalNet.Contracts.Builtins",
                "GalNet.Media.LibVlc",
                "GalNet.Avalonia.Controls", "GalNet.Avalonia.Rendering",
                "GalNet.Presentation.Abstractions", "GalNet.Core", "GalNet.Assets"
            ],
            ["GalNet.Avalonia.Rendering"] = ["GalNet.Contracts.Builtins", "GalNet.Core"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> AllowedTestDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GalNet.Architecture.Tests"] = [],
            ["GalNet.Diagnostics.Tests"] = ["GalNet.Diagnostics"],
            ["GalNet.Debugging.Tests"] = ["GalNet.Debugging", "GalNet.Presentation.Abstractions"],
            ["GalNet.GameHost.Tests"] = ["GalNet.GameHost"],
            ["GalNet.Core.Tests"] = ["GalNet.Core"],
            ["GalNet.Runtime.Tests"] = ["GalNet.Core", "GalNet.Presentation.Abstractions", "GalNet.Runtime"],
            ["GalNet.Primitives.Builtins.Tests"] =
            [
                "GalNet.Core",
                "GalNet.Presentation.Abstractions",
                "GalNet.Primitives.Builtins",
                "GalNet.Contracts.Builtins",
                "GalNet.Runtime"
            ],
            ["GalNet.Assets.Tests"] = ["GalNet.Contracts.Builtins", "GalNet.Assets", "GalNet.Core"],
            ["GalNet.Storage.FileSystem.Tests"] =
            ["GalNet.GameHost", "GalNet.Contracts.Builtins",
                "GalNet.Assets",
                "GalNet.Core",
                "GalNet.Runtime",
                "GalNet.Storage.FileSystem"
            ],
            ["GalNet.Editor.Shared.Tests"] =
            [
                "GalNet.Editor.DefaultContracts",
                "GalNet.Editor.Headless",
                "SchemaA", "SchemaB", "SchemaDuplicate", "SchemaInvalid", "SchemaIncompatible",
                "GalNet.Core",
                "GalNet.Editor.Abstraction",
                "GalNet.Editor.Shared",
                "GalNet.Primitives.Builtins",
                "GalNet.Contracts.Builtins"
            ],
            ["GalNet.Editor.Tests"] =
            ["GalNet.GameHost",
                "GalNet.SchemaGenerator",
                "SchemaA", "SchemaB",
                "GalNet.Core",
                "GalNet.Editor",
                "GalNet.Editor.Abstraction",
                "GalNet.Editor.Shared",
                "GalNet.Primitives.Builtins",
                "GalNet.Contracts.Builtins"
            ],
            ["GalNet.Presentation.Tests"] =
            ["GalNet.Contracts.Builtins", "GalNet.Media.Miniaudio",
                "GalNet.Media.LibVlc",
                "GalNet.SchemaGenerator",
                "GalNet.Assets",
                "GalNet.Avalonia.Controls",
                "GalNet.Avalonia.GameView",
                "GalNet.Avalonia.Themes",
                "GalNet.Avalonia.Rendering",
                "GalNet.Core",
                "GalNet.Presentation.Abstractions",
                "GalNet.Runtime"
            ],
            ["GalNet.Media.Audio.Tests"] = ["GalNet.Media.Miniaudio", "GalNet.Primitives.Builtins"],
            ["GalNet.SchemaGenerator.Tests"] = ["GalNet.SchemaGenerator", "GalNet.Core"],
            ["GalNet.IntegrationTests"] =
            ["GalNet.GameHost",
                "GalNet.Assets",
                "GalNet.Core",
                "GalNet.Editor.Abstraction",
                "GalNet.Editor.Shared",
                "GalNet.Presentation.Abstractions",
                "GalNet.Primitives.Builtins",
                "GalNet.Contracts.Builtins",
                "GalNet.Runtime",
                "GalNet.Sample.Avalonia",
                "GalNet.Storage.FileSystem"
            ]
        };

    public static IEnumerable<TestCaseData> InnerProjectCases() =>
        AllowedDependencies.Keys.Select(project => new TestCaseData(project).SetName($"{project}_UsesOnlyAllowedProjectDependencies"));

    [TestCaseSource(nameof(InnerProjectCases))]
    public void InnerProject_UsesOnlyAllowedProjectDependencies(string projectName)
    {
        var actual = ReadProjectReferences(FindProjectFile(projectName));
        var unexpected = actual.Except(AllowedDependencies[projectName], StringComparer.Ordinal).Order().ToArray();

        Assert.That(
            unexpected,
            Is.Empty,
            $"{projectName} has forbidden direct project dependencies: {string.Join(", ", unexpected)}");
    }

    public static IEnumerable<TestCaseData> TestProjectCases() =>
        AllowedTestDependencies.Keys.Select(project => new TestCaseData(project).SetName($"{project}_UsesOnlyAllowedProjectDependencies"));

    [TestCaseSource(nameof(TestProjectCases))]
    public void TestProject_UsesOnlyAllowedProjectDependencies(string projectName)
    {
        var actual = ReadProjectReferences(FindTestProjectFile(projectName));
        var unexpected = actual.Except(AllowedTestDependencies[projectName], StringComparer.Ordinal).Order().ToArray();

        Assert.That(
            unexpected,
            Is.Empty,
            $"{projectName} has forbidden direct project dependencies: {string.Join(", ", unexpected)}");
    }

    [Test]
    public void TestProjects_DoNotReferenceOtherTestProjects()
    {
        var testProjectNames = AllowedTestDependencies.Keys.ToHashSet(StringComparer.Ordinal);
        var consumers = AllowedTestDependencies.Keys
            .Select(project => (Project: project, References: ReadProjectReferences(FindTestProjectFile(project))))
            .SelectMany(item => item.References
                .Where(testProjectNames.Contains)
                .Select(reference => $"{item.Project} -> {reference}"))
            .Order()
            .ToArray();

        Assert.That(consumers, Is.Empty, $"Test projects reference other test projects: {string.Join(", ", consumers)}");
    }

    [TestCase("GalNet.Sample.Contracts", false)]
    [TestCase("GalNet.Contracts.Builtins", false)]
    [TestCase("GalNet.Editor.Tests", true)]
    [TestCase("GalNet.Presentation.Tests", true)]
    public void SchemaGeneratorReferencesAreBuildOnly(string project, bool testProject)
    {
        var file = testProject ? FindTestProjectFile(project) : FindProjectFile(project);
        var reference = XDocument.Load(file).Descendants("ProjectReference")
            .Single(item => ((string?)item.Attribute("Include"))?.Contains("GalNet.SchemaGenerator.csproj", StringComparison.Ordinal) == true);
        Assert.That((string?)reference.Attribute("OutputItemType"), Is.EqualTo("Analyzer"));
        Assert.That((string?)reference.Attribute("ReferenceOutputAssembly"), Is.EqualTo("false"));
    }

    [Test]
    public void StorageAbstractions_IsAbsentFromTheProductionProjectGraph()
    {
        var projects = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories).ToArray();
        var projectNames = projects.Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();
        var consumers = projects
            .Where(project => ReadProjectReferences(project).Contains("GalNet.Storage.Abstractions", StringComparer.Ordinal))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(projectNames, Does.Not.Contain("GalNet.Storage.Abstractions"));
            Assert.That(consumers, Is.Empty);
        });
    }

    [TestCase("GalNet.GameHost", "GalNet.GameHost")]
    [TestCase("GalNet.Media.LibVlc", "GalNet.Media.LibVlc")]
    [TestCase("GalNet.Presentation.Abstractions", "GalNet.Presentation.Abstractions")]
    [TestCase("GalNet.Primitives.Builtins", "GalNet.Primitives.Builtins")]
    [TestCase("GalNet.Contracts.Builtins", "GalNet.Contracts.Builtins")]
    public void ExtensionProject_DeclaresOnlyOwnedNamespaces(string projectName, string namespaceRoot)
    {
        var projectDirectory = Path.GetDirectoryName(FindProjectFile(projectName))!;
        var unexpected = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, projectDirectory))
            .SelectMany(path => File.ReadLines(path)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("namespace ", StringComparison.Ordinal))
                .Select(line => (Path: path, Namespace: ReadNamespace(line))))
            .Where(declaration =>
                !string.Equals(declaration.Namespace, namespaceRoot, StringComparison.Ordinal) &&
                !declaration.Namespace.StartsWith($"{namespaceRoot}.", StringComparison.Ordinal))
            .Select(declaration => $"{Path.GetRelativePath(RepositoryRoot, declaration.Path)}: {declaration.Namespace}")
            .Order()
            .ToArray();

        Assert.That(
            unexpected,
            Is.Empty,
            $"{projectName} declares namespaces outside its owned root: {string.Join(", ", unexpected)}");
    }

    [TestCase("GalNet.GameHost")]
    [TestCase("GalNet.Media.LibVlc")]
    public void SharedHostAndAudioBackend_DoNotReferenceUiPackages(string project)
    {
        var packages = XDocument.Load(FindProjectFile(project)).Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? "").ToArray();
        Assert.That(packages.Where(name => name.StartsWith("Avalonia", StringComparison.Ordinal) ||
            name.StartsWith("SkiaSharp", StringComparison.Ordinal) || name.EndsWith(".Avalonia", StringComparison.Ordinal)), Is.Empty);
    }

    [Test]
    public void Runtime_DoesNotPerformFileSystemIo()
    {
        var directory = Path.GetDirectoryName(FindProjectFile("GalNet.Runtime"))!;
        var forbidden = new System.Text.RegularExpressions.Regex(@"\b(?:File|Directory)\s*\.");
        var violations = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, directory))
            .Where(path => forbidden.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path)).ToArray();
        Assert.That(violations, Is.Empty, "Runtime must consume content and persistence ports instead of paths.");
    }

    private static string FindProjectFile(string projectName)
    {
        var directory = Path.Combine(RepositoryRoot, "src");
        var matches = Directory.EnumerateFiles(directory, $"{projectName}.csproj", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path, directory)).ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected one project file for {projectName}.");
        return matches[0];
    }

    private static string FindTestProjectFile(string projectName)
    {
        var directory = Path.Combine(RepositoryRoot, "test");
        var matches = Directory.EnumerateFiles(directory, $"{projectName}.csproj", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path, directory)).ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected one test project file for {projectName}.");
        return matches[0];
    }

    private static string[] ReadProjectReferences(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            // MSBuild accepts both separators on every host.
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!.Replace('\\', '/'))))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();
    }

    private static bool IsBuildOutput(string path, string projectDirectory)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, path);
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadNamespace(string declaration) =>
        declaration["namespace ".Length..].TrimEnd(';', '{').Trim();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GalNet.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the GalNet repository root from the test output directory.");
    }
}
