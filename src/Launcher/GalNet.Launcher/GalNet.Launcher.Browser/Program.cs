using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

namespace GalNet.Launcher.Browser;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
        .WithInterFont()
#if DEBUG
        .WithDeveloperTools()
#endif
        .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}