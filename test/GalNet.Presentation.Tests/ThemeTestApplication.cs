using Avalonia;
using Avalonia.Headless;
using GalNet.Avalonia.Themes;
[assembly: AvaloniaTestApplication(typeof(GeneralTest.Presentation.ThemeTestApplication))]
namespace GeneralTest.Presentation;

public static class ThemeTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia()
        .With(GameTypography.CreateFontManagerOptions())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
