using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MainWindowPreparesStartupBeforeShowAndDoesNotResetNavigationOnLoad(bool navigateBeforeShow)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(() =>
        {
            PrepareGroupTheme();
            using var services = Services();
            var navigation = services.GetRequiredService<INavigationService>();
            var window = services.GetRequiredService<MainWindow>();
            Assert.That(navigation.CurrentPage, Is.TypeOf<StartupPageViewModel>());
            Assert.That(window.FindControl<ContentControl>("PageHost")!.Content, Is.TypeOf<StartupPageView>());
            if (navigateBeforeShow) navigation.NavigateTo<NewProjectPanelViewModel>();
            var expected = navigation.CurrentPage;
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.That(navigation.CurrentPage, Is.SameAs(expected));
                Assert.That(((Control)window.FindControl<ContentControl>("PageHost")!.Content!).DataContext,
                    Is.SameAs(expected));
            }
            finally { window.Close(); }
            return Task.FromResult(true);
        }, CancellationToken.None);
    }
}
