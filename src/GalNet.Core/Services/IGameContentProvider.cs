using GameGraph = GalNet.Core.Graph.Graph;

namespace GalNet.Core.Services;

/// <summary>Host supplied game content. Implementations may read a package or build content in memory.</summary>
public interface IGameContentProvider
{
    Task<GameContent> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class GameContent
{
    public required GameGraph Graph { get; init; }
    public string? AssetRoot { get; init; }
}
