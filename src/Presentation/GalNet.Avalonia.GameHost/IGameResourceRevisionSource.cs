using GalNet.Storage.FileSystem;

namespace GalNet.Avalonia.GameHost;

/// <summary>Optional debug-only selector. Returned resources are borrowed for the new run.</summary>
public interface IGameResourceRevisionSource
{
    GameResourceScope SelectForNewRun(GameResourceScope initial);
}
