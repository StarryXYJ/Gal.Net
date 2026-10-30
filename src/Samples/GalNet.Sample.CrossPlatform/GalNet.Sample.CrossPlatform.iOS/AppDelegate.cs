using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using GalNet.Avalonia.Themes;
using GalNet.Sample.Player;

namespace GalNet.Sample.CrossPlatform.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the 
// User Interface of the application, as well as listening (and optionally responding) to 
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var privateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        App.PrepareLaunchAsync = async token =>
        {
            await using var package = File.OpenRead(Path.Combine(NSBundle.MainBundle.BundlePath, "game.galpak"));
            var content = await BundledGameInstaller.InstallAsync(package, Path.Combine(privateRoot, "content"), token);
            var profile = SamplePlayerPaths.GetProfileDirectory(content, Path.Combine(privateRoot, "profiles"));
            return new PlayerLaunchPaths(content, profile, Path.Combine(profile, "screenshots"));
        };
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(GameTypography.CreateFontManagerOptions("fonts:Inter#Inter"));
    }
}
