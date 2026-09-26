using GameGraph = GalNet.Core.Graph.Graph;
using GalNet.Core.Gallery;

namespace GalNet.Core.Services;

/// <summary>Host supplied game content, loaded from a package or built in memory.</summary>
public interface IGameContentProvider
{
    Task<GameContent> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class GameContent
{
    public required GameGraph Graph { get; init; }
    public string? AssetRoot { get; init; }
    public GalleryCatalog Gallery { get; init; } = GalleryCatalog.Empty;
}
