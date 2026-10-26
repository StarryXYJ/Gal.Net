using GalNet.Contracts.Builtins.Gallery;
using GalNet.GameHost;
using GalNet.GameHost.Gallery;
using GalNet.Runtime.Variables;

namespace GalNet.Storage.FileSystem;

/// <summary>Default file-system composition of player persistence.</summary>
public static class FilePlayerSession
{
    public static async Task<PlayerSession> OpenAsync(string profileDirectory, GalleryCatalog gallery,
        int maxSlots = 60, CancellationToken cancellationToken = default)
    {
        var saves = new FileSaveService(profileDirectory, maxSlots);
        PlayerVariableService? variables = null;
        FileGameProgressService? progress = null;
        try
        {
            variables = await PlayerVariableService.CreateAsync(new FilePlayerVariableStore(profileDirectory), cancellationToken);
            variables.ConfigureSystemVariables(GalleryUnlockVariable.CreateDefinitions(gallery));
            progress = new FileGameProgressService(profileDirectory);
            var ownedProgress = progress;
            return new PlayerSession(saves, variables, progress, new GalleryDataSource(gallery, variables), async token =>
            {
                await saves.ClearAsync(token);
                ownedProgress.Clear();
            });
        }
        catch (Exception error)
        {
            var cleanup = new ResourceCleanup();
            cleanup.Run(() => progress?.Dispose());
            cleanup.Run(() => variables?.Dispose());
            cleanup.Run(saves.Dispose);
            cleanup.ThrowIfFailed(error);
            throw;
        }
    }
}
