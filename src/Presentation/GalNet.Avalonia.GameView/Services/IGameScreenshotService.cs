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

/// <summary>Optional platform destination, such as a photo library, that needs no directory picker.</summary>
public interface IAutomaticGameScreenshotDestination
{
    Task SaveAsync(string gameTitle, string fileName, ReadOnlyMemory<byte> png, CancellationToken cancellationToken = default);
}

public sealed record GameScreenshotRequest(
    string GameTitle,
    TopLevel? Owner,
    Func<bool, Task<byte[]>> CapturePngAsync)
{
    public Func<bool, Task<byte[]>>? CapturePreviewPngAsync { get; init; }
}

/// <summary>Optional host integration for capturing the scene associated with a save.</summary>
public interface ISavePreviewSource
{
    Func<byte[]?>? CaptureSavePreview { get; set; }
}
