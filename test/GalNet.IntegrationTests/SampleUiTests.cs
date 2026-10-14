using Avalonia.Headless;
using GeneralTest.Integration;

[assembly: AvaloniaTestApplication(typeof(ScreenshotTestApplication))]

namespace GalNet.IntegrationTests;

internal static class SampleUiTests
{
    public static Task RunAsync(Func<Task> action) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SampleUiTests).Assembly)
            .Dispatch<bool>(async () => { await action(); return true; }, TestContext.CurrentContext.CancellationToken);
}
