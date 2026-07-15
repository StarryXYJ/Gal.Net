using GalNet.Core.Settings;
using GalNet.Core.UI;
using GalNet.Editor.Abstraction.Project;
using GalNet.Editor.Shared.UI;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Control.Tests;

public sealed class ProjectLifecycleTests
{
    [Test]
    public async Task ProjectWaitsForRegisteredCleanupBeforeDisposingScope()
    {
        using var scope = new TestScope();
        var project = new GalProject("test", "test", Path.GetTempPath(), new ProjectSettings(), new EditorProjectState(), new FileUiProjectProvider(Path.GetTempPath()), scope);
        var cleanupFinished = false;
        project.RegisterClosingCallback(async () =>
        {
            await Task.Yield();
            cleanupFinished = true;
        });

        await project.DisposeAsync();

        Assert.That(cleanupFinished, Is.True);
    }

    private sealed class TestScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new EmptyServiceProvider();
        public void Dispose() { }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
