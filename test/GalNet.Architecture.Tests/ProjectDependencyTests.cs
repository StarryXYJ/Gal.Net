using System.Xml.Linq;

namespace GeneralTest.Architecture;

public class ProjectDependencyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly IReadOnlyDictionary<string, string[]> AllowedDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["GalNet.Core"] = [],
            ["GalNet.Presentation.Abstractions"] = ["GalNet.Core"],
            ["GalNet.Runtime"] = ["GalNet.Core", "GalNet.Presentation.Abstractions"],
            ["GalNet.Primitives.Builtins"] = ["GalNet.Core", "GalNet.Presentation.Abstractions"],
            ["GalNet.Assets"] = ["GalNet.Core"],
            ["GalNet.Storage.FileSystem"] = ["GalNet.Core", "GalNet.Runtime", "GalNet.Assets"],
            ["GalNet.Editor.Abstraction"] = ["GalNet.Core"],
            ["GalNet.Editor.Shared"] =
            [
                "GalNet.Core",
                "GalNet.Editor.Abstraction",
                "GalNet.Primitives.Builtins",
                "GalNet.Runtime",
                "GalNet.Assets"
            ],
            ["GalNet.Avalonia.Controls"] = ["GalNet.Core"],
            ["GalNet.Avalonia.Rendering"] = ["GalNet.Core"],
            ["GalNet.Presentation.Defaults"] = ["GalNet.Presentation.Abstractions"]
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

    [TestCase("GalNet.Presentation.Abstractions", "GalNet.Presentation.Abstractions")]
    [TestCase("GalNet.Primitives.Builtins", "GalNet.Primitives.Builtins")]
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

    private static string FindProjectFile(string projectName)
    {
        var matches = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), $"{projectName}.csproj", SearchOption.AllDirectories).ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected one project file for {projectName}.");
        return matches[0];
    }

    private static string[] ReadProjectReferences(string projectFile)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        return XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Distinct(StringComparer.Ordinal)
            .Order()
            .ToArray();
    }

    private static bool IsBuildOutput(string path, string projectDirectory)
    {
        var relativePath = Path.GetRelativePath(projectDirectory, path);
        return relativePath.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
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
