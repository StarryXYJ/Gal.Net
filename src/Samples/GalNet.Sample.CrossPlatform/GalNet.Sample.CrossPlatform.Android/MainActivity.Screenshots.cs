using Android.Content.PM;

namespace GalNet.Sample.CrossPlatform.Android;

public partial class MainActivity
{
    private const int ScreenshotStoragePermission = 3917;
    private static WeakReference<MainActivity>? _current;
    private TaskCompletionSource<bool>? _screenshotPermission;

    internal static async Task<bool> RequestScreenshotStorageAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) return true;
        if (_current is not { } reference || !reference.TryGetTarget(out var activity))
            throw new InvalidOperationException("The Android activity is unavailable.");
        const string permission = global::Android.Manifest.Permission.WriteExternalStorage;
        if (activity.CheckSelfPermission(permission) == Permission.Granted) return true;
        var completion = activity._screenshotPermission;
        if (completion is null)
        {
            completion = activity._screenshotPermission = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try { activity.RequestPermissions([permission], ScreenshotStoragePermission); }
            catch { activity._screenshotPermission = null; throw; }
        }
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != ScreenshotStoragePermission) return;
        var completion = _screenshotPermission;
        _screenshotPermission = null;
        completion?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
    }
}
