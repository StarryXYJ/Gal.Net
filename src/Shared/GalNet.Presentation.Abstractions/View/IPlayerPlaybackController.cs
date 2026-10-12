using GalNet.Core.Primitives;

namespace GalNet.Presentation.Abstractions.View;

/// <summary>Player commands without a dependency on an engine or UI implementation.</summary>
public interface IPlayerPlaybackController
{
    PlaybackMode PlaybackMode { get; }
    event Action<PlaybackMode>? PlaybackModeChanged;
    void CancelPlayback();
    Task SetPlaybackModeAsync(PlaybackMode mode, CancellationToken cancellationToken = default);
    Task SkipToUnreadAsync(CancellationToken cancellationToken = default);
    Task RefreshPlaybackSettingsAsync(CancellationToken cancellationToken = default);
}
