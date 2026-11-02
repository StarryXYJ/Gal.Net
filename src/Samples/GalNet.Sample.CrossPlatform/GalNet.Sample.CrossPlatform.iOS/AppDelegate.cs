using AVFoundation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.iOS;
using Avalonia.Media;
using Foundation;
using GalNet.Avalonia.Themes;
using GalNet.Contracts.Builtins;
using GalNet.Media.LibVlc;
using GalNet.Runtime.Logging;
using GalNet.Sample.CrossPlatform;
using GalNet.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using UIKit;

namespace GalNet.Sample.CrossPlatform.iOS;

[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    private NSObject? _active;
    private NSObject? _inactive;
    private NSObject? _interruption;
    private NSObject? _route;
    private NSObject? _background;
    private NSObject? _termination;
    private static App? Player => global::Avalonia.Application.Current as App;
    private ProjectOrientationPolicy _orientationPolicy = ProjectOrientationPolicy.Unforced;

    [Export("application:supportedInterfaceOrientationsForWindow:")]
    public UIInterfaceOrientationMask GetSupportedInterfaceOrientations(UIApplication application, UIWindow? forWindow) =>
        _orientationPolicy switch
        {
            ProjectOrientationPolicy.ForceLandscape => UIInterfaceOrientationMask.Landscape,
            ProjectOrientationPolicy.ForcePortrait => UIInterfaceOrientationMask.Portrait,
            _ => UIInterfaceOrientationMask.All
        };

    private Task ApplyProjectOrientationAsync(ProjectOrientationPolicy policy,
        ProjectDefaultOrientation defaultOrientation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = UIApplication.SharedApplication;
        var requested = policy switch
        {
            ProjectOrientationPolicy.ForceLandscape => UIInterfaceOrientationMask.Landscape,
            ProjectOrientationPolicy.ForcePortrait => UIInterfaceOrientationMask.Portrait,
            _ => defaultOrientation == ProjectDefaultOrientation.Landscape
                ? UIInterfaceOrientationMask.Landscape : UIInterfaceOrientationMask.Portrait
        };
        application.BeginInvokeOnMainThread(() =>
        {
            _orientationPolicy = policy;
            if (OperatingSystem.IsIOSVersionAtLeast(16))
            {
                foreach (var scene in application.ConnectedScenes.OfType<UIWindowScene>())
                {
                    scene.RequestGeometryUpdate(new UIWindowSceneGeometryPreferencesIOS(requested), error =>
                        GameLog.Logger.Warning("Could not apply project orientation: {Reason}", error.LocalizedDescription));
                    foreach (var window in scene.Windows)
                        window.RootViewController?.SetNeedsUpdateOfSupportedInterfaceOrientations();
                }
            }
            else UIViewController.AttemptRotationToDeviceOrientation();
        });
        return Task.CompletedTask;
    }

    private static void OnActivated() => Observe(async () =>
    {
        if (Player is not { } player) return;
        await player.SetAudioAvailableAsync(ActivateAudio());
        await player.SetPlatformActiveAsync(true);
    });

    private static void OnDeactivated() =>
        Observe(() => Player?.SetPlatformActiveAsync(false));

    private static void OnDidEnterBackground()
    {
        var application = UIApplication.SharedApplication;
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
        if (disposing)
        {
            _active?.Dispose(); _inactive?.Dispose();
            _interruption?.Dispose(); _route?.Dispose(); _background?.Dispose(); _termination?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.ProjectOrientationHandler = ApplyProjectOrientationAsync;
        _active = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ => OnActivated());
        _inactive = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, _ => OnDeactivated());
        var audio = AVAudioSession.SharedInstance();
        var error = audio.SetCategory(AVAudioSessionCategory.Playback);
        if (error is not null) GameLog.Logger.Error("iOS audio category failed: {Error}", error.LocalizedDescription);
        _interruption = AVAudioSession.Notifications.ObserveInterruption((_, args) =>
        {
            var began = args.InterruptionType == AVAudioSessionInterruptionType.Began;
            BeginInvokeOnMainThread(() => Observe(() => Player?.SetAudioAvailableAsync(!began && ActivateAudio())));
        });
        _route = AVAudioSession.Notifications.ObserveRouteChange((_, args) =>
        {
            if (args.Reason == AVAudioSessionRouteChangeReason.OldDeviceUnavailable)
                BeginInvokeOnMainThread(() => Observe(() => Player?.InterruptAsync()));
        });
        _background = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => OnDidEnterBackground());
        _termination = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillTerminateNotification,
            _ => Observe(() => Player?.ShutdownAsync()));
        var privateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        App.ConfigurePlatformServices = services => services.AddSingleton(new LibVlcFactory(new() { UseSystemLibraries = true }));
        App.PrepareLaunchAsync = async token =>
        {
            await using var package = File.OpenRead(Path.Combine(NSBundle.MainBundle.BundlePath, "game.galpak"));
            var content = await BundledGameInstaller.InstallAsync(package, Path.Combine(privateRoot, "content"), token);
            var profile = SampleCustomization.GetProfileDirectory(content, Path.Combine(privateRoot, "profiles"));
            return new PlayerLaunchPaths(content, profile, Path.Combine(profile, "screenshots"));
        };
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(GameTypography.CreateFontManagerOptions("fonts:Inter#Inter"));
    }
}
