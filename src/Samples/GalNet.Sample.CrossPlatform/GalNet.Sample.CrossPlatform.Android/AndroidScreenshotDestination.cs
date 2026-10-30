using Android.Content;
using Android.Provider;
using GalNet.Avalonia.GameView.Services;
using GalNet.Runtime.Logging;

namespace GalNet.Sample.CrossPlatform.Android;

internal sealed class AndroidScreenshotDestination(Context context) : IAutomaticGameScreenshotDestination
{
    public async Task SaveAsync(string gameTitle, string fileName, ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default)
    {
        if (!await MainActivity.RequestScreenshotStorageAsync(cancellationToken))
            throw new UnauthorizedAccessException("Photo storage permission was not granted.");
        await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolver = context.ContentResolver ?? throw new IOException("The Android media store is unavailable.");
            using var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
            values.Put(MediaStore.IMediaColumns.MimeType, "image/png");
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var folder = new string(gameTitle.Select(character =>
                    character is '/' or '\\' || char.IsControl(character) ? '_' : character).Take(80).ToArray()).Trim(' ', '.');
                values.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/" + (folder.Length == 0 ? "GalNet" : folder));
                values.Put(MediaStore.IMediaColumns.IsPending, 1);
            }
            var collection = OperatingSystem.IsAndroidVersionAtLeast(29)
                ? MediaStore.Images.Media.GetContentUri(MediaStore.VolumeExternalPrimary)
                : MediaStore.Images.Media.ExternalContentUri;
            using var item = resolver.Insert(collection!, values) ?? throw new IOException("Could not create a screenshot in the photo library.");
            try
            {
                await using (var stream = resolver.OpenOutputStream(item) ?? throw new IOException("Could not open the screenshot for writing."))
                {
                    await stream.WriteAsync(png, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (OperatingSystem.IsAndroidVersionAtLeast(29))
                {
                    values.Clear();
                    values.Put(MediaStore.IMediaColumns.IsPending, 0);
                    if (resolver.Update(item, values, null, null) != 1) throw new IOException("Could not publish the screenshot to the photo library.");
                }
                GameLog.Logger.Information("Screenshot saved to Android photo library: {FileName}", fileName);
            }
            catch
            {
                try { resolver.Delete(item, null, null); }
                catch (Exception cleanupError) { GameLog.Logger.Warning(cleanupError, "Could not remove an incomplete screenshot."); }
                throw;
            }
        }, cancellationToken);
    }
}
