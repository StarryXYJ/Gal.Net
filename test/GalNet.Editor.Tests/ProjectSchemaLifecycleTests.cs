using GalNet.Core.Entry;
using GalNet.Core.Settings;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Composition;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed class ProjectSchemaLifecycleTests
{
    private string _root = null!;
    [SetUp]
    public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GalNet-editor-schema-" + Guid.NewGuid())).FullName;
    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public async Task RealEditorComposition_SwitchesCatalogConsumersAndShortcuts_AndDisposesOldContext()
    {
        var settings = new TestSettings();
        using var services = new ServiceCollection().AddEditorServices().AddSingleton<IEditorSettingsService>(settings)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var projects = services.GetRequiredService<IProjectService>();
        var first = await projects.OpenAsync(Deploy("SchemaA"));
        var context = first.Services.GetRequiredService<ProjectSchemaSession>().Context;
        var catalog = first.Services.GetRequiredService<IEntryCatalog>();
        var repository = first.Services.GetRequiredService<IEditorDocumentRepository>();
        var graphEditing = first.Services.GetRequiredService<IGraphEditingService>();
        var shortcuts = first.Services.GetRequiredService<EditorShortcutService>();
        Assert.That(catalog.TryGet("custom.example", out _), Is.True);
        Assert.That(shortcuts.Commands.OfType<EntryTypeShortcutCommand>().Select(command => command.EntryType), Does.Contain("custom.example"));
        var second = await projects.OpenAsync(Deploy("SchemaB"));
        Assert.Multiple(() =>
        {
            Assert.That(() => context.Catalog, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(second.Services.GetRequiredService<IEntryCatalog>().TryGet("custom.example", out _), Is.False);
            Assert.That(second.Services.GetRequiredService<IEditorDocumentRepository>(), Is.Not.SameAs(repository));
            Assert.That(second.Services.GetRequiredService<IGraphEditingService>(), Is.Not.SameAs(graphEditing));
            Assert.That(second.Services.GetRequiredService<EditorShortcutService>().Commands.OfType<EntryTypeShortcutCommand>().Select(command => command.EntryType), Does.Not.Contain("custom.example"));
            Assert.That(() => services.GetRequiredService<IEntryCatalog>(), Throws.InvalidOperationException);
        });
        var secondContext = second.Services.GetRequiredService<ProjectSchemaSession>().Context;
        await projects.CloseAsync();
        Assert.That(projects.Current, Is.Null);
        Assert.That(() => secondContext.Catalog, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task FailedOpen_ClearsPreviousProjectAndDoesNotAddFailedRecentRecord()
    {
        var settings = new TestSettings();
        using var services = new ServiceCollection().AddEditorServices().AddSingleton<IEditorSettingsService>(settings).BuildServiceProvider();
        var projects = services.GetRequiredService<IProjectService>();
        var first = await projects.OpenAsync(Deploy("SchemaA"));
        var context = first.Services.GetRequiredService<ProjectSchemaSession>().Context;
        var missing = Directory.CreateDirectory(Path.Combine(_root, "missing")).FullName;
        Assert.ThrowsAsync<FileNotFoundException>(() => projects.OpenAsync(missing));
        Assert.That(projects.Current, Is.Null);
        Assert.That(() => context.Catalog, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(settings.GetSettings().RecentProjects.Select(item => item.Path), Does.Not.Contain(missing));
    }

    [Test]
    public async Task OpeningListenerFailure_DisposesInitializedScope()
    {
        using var services = new ServiceCollection().AddEditorServices().AddSingleton<IEditorSettingsService>(new TestSettings()).BuildServiceProvider();
        var projects = services.GetRequiredService<IProjectService>();
        ProjectSchemaContext? loaded = null;
        projects.CurrentChanged += project =>
        {
            if (project is null) return;
            loaded = project.Services.GetRequiredService<ProjectSchemaSession>().Context;
            throw new InvalidOperationException("listener failed");
        };
        Assert.ThrowsAsync<InvalidOperationException>(() => projects.OpenAsync(Deploy("SchemaA")));
        Assert.That(projects.Current, Is.Null);
        Assert.That(() => loaded!.Catalog, Throws.TypeOf<ObjectDisposedException>());
        await projects.CloseAsync();
    }

    [Test]
    public async Task Create_UsesSharedCreatorAndProvidedContract_AndOpensGraphDisplayName()
    {
        using var services = new ServiceCollection().AddEditorServices().AddSingleton<IEditorSettingsService>(new TestSettings()).BuildServiceProvider();
        var projects = services.GetRequiredService<IProjectService>();
        var source = Deploy("SchemaA");
        var path = Path.Combine(_root, "Created");
        var project = await projects.CreateAsync(path, "Display Name", new(), Path.Combine(source, "Contracts"));
        Assert.That(project.Name, Is.EqualTo("Display Name"));
        Assert.That(project.Id, Is.EqualTo("Created"));
        Assert.That(project.Services.GetRequiredService<IEntryCatalog>().TryGet("custom.bundle", out _), Is.True);
        await projects.CloseAsync();
        Assert.That((await projects.OpenAsync(path)).Name, Is.EqualTo("Display Name"));
        await projects.CloseAsync();
    }

    private string Deploy(string fixture)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var project = Directory.CreateDirectory(Path.Combine(_root, fixture)).FullName;
        var contracts = Directory.CreateDirectory(Path.Combine(project, "Contracts")).FullName;
        foreach (var file in Directory.GetFiles(Path.Combine(directory.FullName, "test", "Fixtures", fixture, "bin", configuration, "net10.0")))
            File.Copy(file, Path.Combine(contracts, Path.GetFileName(file)));
        Directory.CreateDirectory(Path.Combine(project, "Graph"));
        File.WriteAllText(Path.Combine(project, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(project, "Graph/graph.json"),
            """{"version":2,"name":"Fixture","rootNodeId":"","nodes":[],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        return project;
    }
    private sealed class TestSettings : IEditorSettingsService
    {
        private readonly EditorSettings _settings = new();
        public EditorSettings GetSettings() => _settings;
        public void SaveSettings() { }
    }
}
