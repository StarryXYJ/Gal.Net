using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>
/// Host-replaceable player interaction for saving a screenshot. The game shell supplies pixels;
/// implementations decide how players preview and write them.
/// </summary>
public interface IGameScreenshotService
{
    Task CaptureAsync(GameScreenshotRequest request, CancellationToken cancellationToken = default);
}

public sealed record GameScreenshotRequest(
    string GameTitle,
    TopLevel? Owner,
    Func<bool, Task<byte[]>> CapturePngAsync);
