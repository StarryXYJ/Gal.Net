namespace GalNet.Core.View;

/// <summary>Presentation contract for dialogue typewriter playback.</summary>
public interface ITypewriterView
{
    /// <summary>Starts a cancellable typewriter sequence in the specified dialogue widget.</summary>
    Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct);
    void SkipTypewriter(string widgetInstanceId);
    void SetVoice(string assetId);
}
