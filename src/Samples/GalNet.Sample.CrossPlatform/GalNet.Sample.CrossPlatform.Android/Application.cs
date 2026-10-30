using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using GalNet.Avalonia.Themes;
using GalNet.Sample.Player;
using GalNet.Storage.FileSystem;
using GalNet.Media.LibVlc;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Sample.CrossPlatform.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            // Android may expose its private storage through a system-owned alias.
            // Use the physical sandbox root before applying the content link checks.
            var privateRoot = FilesDir!.CanonicalPath;
            App.ConfigurePlatformServices = services => services.AddSingleton(new LibVlcFactory(new() { UseSystemLibraries = true }));
            App.PrepareLaunchAsync = async token =>
            {
                await using var package = Assets!.Open("game.galpak");
                var content = await BundledGameInstaller.InstallAsync(package, Path.Combine(privateRoot, "content"), token);
                var profile = SamplePlayerPaths.GetProfileDirectory(content, Path.Combine(privateRoot, "profiles"));
                return new PlayerLaunchPaths(content, profile, Path.Combine(profile, "screenshots"));
            };
            return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(GameTypography.CreateFontManagerOptions("fonts:Inter#Inter"));
        }
    }
}
