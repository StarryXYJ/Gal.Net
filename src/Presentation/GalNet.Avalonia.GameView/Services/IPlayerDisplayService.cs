namespace GalNet.Avalonia.GameView.Services;

public enum PlayerDisplayMode { Window, BorderlessFullscreen, ExclusiveFullscreen }
public readonly record struct PlayerWindowSize(int Width, int Height);

/// <summary>Host display capability and actual state; never changes the project's design resolution.</summary>
public interface IPlayerDisplayService
{
    bool IsAvailable { get; }
    IReadOnlyList<PlayerDisplayMode> Modes { get; }
    IReadOnlyList<PlayerWindowSize> WindowSizes { get; }
    PlayerDisplayMode Mode { get; }
    PlayerWindowSize WindowSize { get; }
    event Action? Changed;
    Task ApplyModeAsync(PlayerDisplayMode mode, CancellationToken cancellationToken);
    Task ApplyWindowSizeAsync(PlayerWindowSize size, CancellationToken cancellationToken);
}
