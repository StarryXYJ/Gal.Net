using Foundation;
using UIKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using GalNet.Avalonia.Themes;
using GalNet.Sample.Player;
using GalNet.Media.LibVlc;
using Microsoft.Extensions.DependencyInjection;
using AVFoundation;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.CrossPlatform.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the 
// User Interface of the application, as well as listening (and optionally responding) to 
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    private NSObject? _interruption;
    private NSObject? _route;
    private static App? Player => global::Avalonia.Application.Current as App;

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);
        var audio = AVAudioSession.SharedInstance();
        audio.SetCategory(AVAudioSessionCategory.Playback, out var error);
        if (error is not null) GameLog.Logger.Error("iOS audio category failed: {Error}", error.LocalizedDescription);
        _interruption = NSNotificationCenter.DefaultCenter.AddObserver(AVAudioSession.InterruptionNotification, notification =>
        {
            var began = (notification.UserInfo?[AVAudioSession.InterruptionTypeKey] as NSNumber)?.UInt64Value == (ulong)AVAudioSessionInterruptionType.Began;
            BeginInvokeOnMainThread(() => Observe(() => Player?.SetAudioAvailableAsync(!began && ActivateAudio())));
        });
        _route = NSNotificationCenter.DefaultCenter.AddObserver(AVAudioSession.RouteChangeNotification, notification =>
        {
            if ((notification.UserInfo?[AVAudioSession.RouteChangeReasonKey] as NSNumber)?.UInt64Value == (ulong)AVAudioSessionRouteChangeReason.OldDeviceUnavailable)
                BeginInvokeOnMainThread(() => Observe(() => Player?.InterruptAsync()));
        });
        return result;
    }

    public override void OnActivated(UIApplication application)
    {
        base.OnActivated(application);
        Observe(async () =>
        {
            if (Player is not { } player) return;
            await player.SetAudioAvailableAsync(ActivateAudio());
            await player.SetPlatformActiveAsync(true);
        });
    }

    public override void OnResignActivation(UIApplication application)
    {
        Observe(() => Player?.SetPlatformActiveAsync(false));
        base.OnResignActivation(application);
    }

    public override void DidEnterBackground(UIApplication application)
    {
        var background = UIApplication.BackgroundTaskInvalid;
        background = application.BeginBackgroundTask("GalNet checkpoint", () =>
        {
            if (background == UIApplication.BackgroundTaskInvalid) return;
            application.EndBackgroundTask(background);
            background = UIApplication.BackgroundTaskInvalid;
        });
        Observe(async () =>
        {
            try { if (Player is { } player) await player.SetPlatformActiveAsync(false); }
            finally
            {
                if (background != UIApplication.BackgroundTaskInvalid) application.EndBackgroundTask(background);
                background = UIApplication.BackgroundTaskInvalid;
            }
        });
        base.DidEnterBackground(application);
    }

    public override void WillTerminate(UIApplication application)
    {
        Observe(() => Player?.ShutdownAsync());
        base.WillTerminate(application);
    }

    private static bool ActivateAudio()
    {
        var success = AVAudioSession.SharedInstance().SetActive(true, out var error);
        if (error is not null) GameLog.Logger.Error("iOS audio activation failed: {Error}", error.LocalizedDescription);
        return success;
    }

    private static async void Observe(Func<Task?> action)
    {
        try { if (action() is { } task) await task; }
        catch (Exception error) { GameLog.Logger.Error(error, "iOS player lifecycle failed"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _interruption?.Dispose(); _route?.Dispose(); }
        base.Dispose(disposing);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var privateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        App.ConfigurePlatformServices = services => services.AddSingleton(new LibVlcFactory(new() { UseSystemLibraries = true }));
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
