namespace GalNet.Storage.FileSystem;

/// <summary>Replaces one file through a unique sibling temporary file; callers own serialization and synchronization.</summary>
internal static class FileReplacement
{
    public static async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".write-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await FilePublication.MoveFileAsync(temporary, path, overwrite: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception writeError)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanupError) { throw new AggregateException(writeError, cleanupError); }
            throw;
        }
    }
}
