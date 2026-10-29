using Avalonia.Controls;
using Avalonia.Headless;
using GalNet.Assets;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [TestCase("package")]
    [TestCase("directory")]
    public async Task ContentExportMountedPanelOffersOnlyContentAndExportsOutsideRepository(string mode)
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            using var services = Services();
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "Source"), "Content", new(),
                Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var vm = project.Services.GetRequiredService<ExportPanelViewModel>();
            vm.OutputDirectory = Path.Combine(_root, "export with spaces");
            vm.SelectedMode = vm.Modes.Single(option => option.Key == mode);
            var view = new ExportPanelView { DataContext = vm };
            var window = new Window { Content = view, Width = 560, Height = 420 };
            window.Show(); window.UpdateLayout();
            try
            {
                Assert.That(vm.Modes.Select(option => option.Key), Is.EqualTo(new[] { "package", "directory" }));
                var selector = view.FindControl<ComboBox>("ExportModeSelector")!;
                Assert.That(selector.SelectedItem, Is.EqualTo(vm.SelectedMode));
                Assert.That(view.FindControl<TextBox>("ExportOutputInput")!.Text, Is.EqualTo(vm.OutputDirectory));
                var result = await vm.ExportAsync(CancellationToken.None);
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(result.PackagePath, Is.EqualTo(vm.PackagePath));
                Assert.That(vm.IsExporting, Is.False);
                var installation = await GameInstallation.OpenAsync(result.PackagePath!);
                var packed = PackedGameDocuments.ReadGame(File.ReadAllBytes(Path.Combine(installation.RootDirectory, "game.pak")));
                Assert.That(packed.Manifest.ProjectId, Is.EqualTo(project.Id));
                Assert.That(packed.Installation.Version, Is.EqualTo(4));
                if (mode == "directory")
                {
                    var before = File.ReadAllBytes(Path.Combine(vm.PackagePath, "game.pak"));
                    var repeated = await vm.ExportAsync(CancellationToken.None);
                    Assert.That(repeated.Success, Is.False);
                    Assert.That(File.ReadAllBytes(Path.Combine(vm.PackagePath, "game.pak")), Is.EqualTo(before));
                }
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
