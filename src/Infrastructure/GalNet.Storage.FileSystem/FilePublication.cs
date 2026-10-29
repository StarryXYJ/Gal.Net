namespace GalNet.Storage.FileSystem;

/// <summary>Publishes staged paths atomically, tolerating short Windows handle-sharing conflicts.</summary>
public static class FilePublication
{
    /// <summary>Moves a directory without replacing an existing destination.</summary>
    public static Task MoveDirectoryAsync(string source, string destination, CancellationToken cancellationToken = default) =>
        MoveAsync(() => Directory.Move(source, destination), cancellationToken);

    /// <summary>Moves a file, optionally replacing the destination in one filesystem operation.</summary>
    public static Task MoveFileAsync(string source, string destination, bool overwrite = false, CancellationToken cancellationToken = default) =>
        MoveAsync(() => File.Move(source, destination, overwrite), cancellationToken);

    private static async Task MoveAsync(Action move, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { move(); return; }
            catch (Exception error) when (attempt < 5 && IsWindowsSharingConflict(error))
            {
                // Windows directory renames can report ACCESS_DENIED rather than
                // SHARING_VIOLATION while another handle temporarily excludes delete.
                // Six attempts span at most 620 ms; persistent denial still fails.
                await Task.Delay(20 << attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsWindowsSharingConflict(Exception error) => OperatingSystem.IsWindows()
        && error is IOException or UnauthorizedAccessException
        && (error.HResult & 0xffff) is 5 or 32 or 33;
}
