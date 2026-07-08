namespace GalNet.Core.Services;

/// <summary>
/// Provides game data (graph.json, .galgroup files) for the game runtime.
/// Editor implements this to generate preview data on-the-fly.
/// A pure game implements this to point to the game's data directory.
/// </summary>
public interface IGameDataProvider
{
    /// <summary>Directory containing graph.json and .galgroup files.</summary>
    string DataDirectory { get; }
}