using GalNet.Core.Gallery;
using GalNet.Core.Settings;
using GameGraph = GalNet.Core.Graph.Graph;

namespace GalNet.Runtime.Content;

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
    public ProjectSettings Settings { get; init; } = new();
    public GameLocalizationContent Localization { get; init; } = new();
}
