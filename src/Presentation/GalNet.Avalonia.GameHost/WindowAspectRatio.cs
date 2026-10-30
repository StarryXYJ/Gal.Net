using Avalonia;

namespace GalNet.Avalonia.GameHost;

/// <summary>Fits the game content, excluding window chrome, within logical screen dimensions.</summary>
public static class WindowAspectRatio
{
    public static Size Fit(Size content, Size chrome, Size minimumWindow, Size availableWindow, double ratio)
    {
        if (!double.IsFinite(ratio) || ratio <= 0) throw new ArgumentOutOfRangeException(nameof(ratio));
        var availableWidth = Math.Max(1, availableWindow.Width - chrome.Width);
        var availableHeight = Math.Max(1, availableWindow.Height - chrome.Height);
        var width = Math.Min(content.Width, content.Height * ratio);
        var minimumWidth = Math.Max(Math.Max(0, minimumWindow.Width - chrome.Width),
            Math.Max(0, minimumWindow.Height - chrome.Height) * ratio);
        width = Math.Min(Math.Max(width, minimumWidth), Math.Min(availableWidth, availableHeight * ratio));
        return new Size(width + chrome.Width, width / ratio + chrome.Height);
    }
}
