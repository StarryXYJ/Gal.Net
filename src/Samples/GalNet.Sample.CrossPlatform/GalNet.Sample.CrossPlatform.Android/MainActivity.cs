using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Avalonia.Threading;
using Android.Content;
using Android.Media;
using Android.OS;
using GalNet.Runtime.Logging;
using GalNet.Contracts.Builtins;

namespace GalNet.Sample.CrossPlatform.Android;

[Activity(
    Label = "GalNet.Sample.CrossPlatform.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public partial class MainActivity : AvaloniaMainActivity, AudioManager.IOnAudioFocusChangeListener
{
    private AudioManager? _audio;
    private AudioFocusRequestClass? _focus;
    private NoisyReceiver? _noisy;
    private static App? Player => global::Avalonia.Application.Current as App;
    private bool _releaseDefaultOrientationAfterChange;
    private global::Android.Content.Res.Orientation _pendingDefaultOrientation;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        App.ProjectOrientationHandler = ApplyProjectOrientationAsync;
        base.OnCreate(savedInstanceState);
        InitializeChineseFontFallback();
        _current = new(this);
        _audio = (AudioManager?)GetSystemService(AudioService);
        if (Player is { } player) player.ExitRequested = () => MoveTaskToBack(true);
        _noisy = new NoisyReceiver(() => Observe(() => Player?.InterruptAsync()));
        var filter = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) RegisterReceiver(_noisy, filter, ReceiverFlags.NotExported);
        else RegisterReceiver(_noisy, filter);
        Observe(() => App.ApplyProjectOrientationAsync(App.CurrentOrientationPolicy, App.CurrentDefaultOrientation, CancellationToken.None));
    }

    private static void InitializeChineseFontFallback()
    {
        // Resolve these before the first player layout: on a tested Android device,
        // normal CJK text otherwise rendered as missing-glyph bars.
        foreach (var weight in new[] { global::Avalonia.Media.FontWeight.Normal, global::Avalonia.Media.FontWeight.Bold })
        {
            if (global::Avalonia.Media.FontManager.Current.TryMatchCharacter('游', global::Avalonia.Media.FontStyle.Normal, weight,
                global::Avalonia.Media.FontStretch.Normal, global::Avalonia.Media.FontFamily.Default,
                System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), out var face))
                _ = face.GlyphTypeface.CharacterToGlyphMap.GetGlyph('游');
        }
    }

    private static async Task ApplyProjectOrientationAsync(ProjectOrientationPolicy policy,
        ProjectDefaultOrientation defaultOrientation, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_current is { } reference && reference.TryGetTarget(out var activity))
                activity.ApplyOrientation(policy, defaultOrientation);
        }, DispatcherPriority.Normal, cancellationToken);
    }

    private void ApplyOrientation(ProjectOrientationPolicy policy, ProjectDefaultOrientation defaultOrientation)
    {
        _releaseDefaultOrientationAfterChange = false;
        if (policy == ProjectOrientationPolicy.ForceLandscape)
        {
            RequestedOrientation = ScreenOrientation.SensorLandscape;
            return;
        }
        if (policy == ProjectOrientationPolicy.ForcePortrait)
        {
            RequestedOrientation = ScreenOrientation.SensorPortrait;
            return;
        }

        _pendingDefaultOrientation = defaultOrientation == ProjectDefaultOrientation.Landscape
            ? global::Android.Content.Res.Orientation.Landscape
            : global::Android.Content.Res.Orientation.Portrait;
        if (Resources?.Configuration?.Orientation == _pendingDefaultOrientation)
        {
            RequestedOrientation = ScreenOrientation.Unspecified;
            return;
        }

        _releaseDefaultOrientationAfterChange = true;
        RequestedOrientation = defaultOrientation == ProjectDefaultOrientation.Landscape
            ? ScreenOrientation.Landscape : ScreenOrientation.Portrait;
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (!_releaseDefaultOrientationAfterChange || newConfig.Orientation != _pendingDefaultOrientation) return;
        _releaseDefaultOrientationAfterChange = false;
        RequestedOrientation = ScreenOrientation.Unspecified;
    }

    protected override void OnResume()
    {
        base.OnResume();
        var granted = RequestFocus();
        Observe(() => Player?.SetPlatformActiveAsync(true, granted));
    }

    protected override void OnPause()
    {
        Observe(() => Player?.SetPlatformActiveAsync(false));
        AbandonFocus();
        base.OnPause();
    }

    public void OnAudioFocusChange(AudioFocus focusChange) => RunOnUiThread(() =>
        Observe(() => Player?.SetAudioAvailableAsync(focusChange == AudioFocus.Gain)));

    private bool RequestFocus()
    {
        if (_audio is null) return false;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (_focus is null)
            {
                using var attributes = new AudioAttributes.Builder();
                attributes.SetUsage(AudioUsageKind.Game);
                attributes.SetContentType(AudioContentType.Music);
                using var request = new AudioFocusRequestClass.Builder(AudioFocus.Gain);
                request.SetAudioAttributes(attributes.Build()!);
                request.SetWillPauseWhenDucked(true);
                request.SetOnAudioFocusChangeListener(this);
                _focus = request.Build();
            }
            return _audio.RequestAudioFocus(_focus!) == AudioFocusRequest.Granted;
        }
#pragma warning disable CS0618, CA1422 // Android 23-25 require the legacy focus API.
        return _audio.RequestAudioFocus(this, global::Android.Media.Stream.Music, AudioFocus.Gain) == AudioFocusRequest.Granted;
#pragma warning restore CS0618, CA1422
    }

    private void AbandonFocus()
    {
        if (_audio is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && _focus is not null) _audio.AbandonAudioFocusRequest(_focus);
#pragma warning disable CS0618, CA1422
        else _audio.AbandonAudioFocus(this);
#pragma warning restore CS0618, CA1422
    }

    protected override void OnDestroy()
    {
        if (_current is { } reference && reference.TryGetTarget(out var current) && ReferenceEquals(current, this)) _current = null;
        _screenshotPermission?.TrySetCanceled();
        if (_noisy is not null) { UnregisterReceiver(_noisy); _noisy.Dispose(); _noisy = null; }
        AbandonFocus();
        // Finishing an Activity does not terminate the Android application process.
        // Keep its shared session reusable when the launcher creates another Activity.
        if (IsFinishing) Observe(() => Player?.InterruptAsync());
        _focus?.Dispose();
        base.OnDestroy();
    }

    private static async void Observe(Func<Task?> action)
    {
        try { if (action() is { } task) await task; }
        catch (Exception error) { GameLog.Logger.Error(error, "Android player lifecycle failed"); }
    }

    private sealed class NoisyReceiver(Action interrupt) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy) interrupt();
        }
    }
}
