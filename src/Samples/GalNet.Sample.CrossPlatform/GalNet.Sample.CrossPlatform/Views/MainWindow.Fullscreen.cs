using Avalonia.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class MainWindow
{
    private const uint NonClientHitTest = 0x0084;
    private const uint NonClientLeftButtonDown = 0x00A1;
    private const uint NonClientLeftButtonDoubleClick = 0x00A3;
    private const uint SystemCommand = 0x0112;
    private const long MoveCommand = 0xF010;
    private const long SizeCommand = 0xF000;
    private const int ClientArea = 1;
    private const int CaptionArea = 2;

    private nint FilterFullscreenMessages(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (WindowState != WindowState.FullScreen) return 0;
        // Hiding the drawn caption does not guard native move commands issued by
        // other chrome paths. Fullscreen has no draggable or resizable non-client area.
        if (message == NonClientHitTest)
        {
            handled = true;
            return ClientArea;
        }
        if ((message is NonClientLeftButtonDown or NonClientLeftButtonDoubleClick && wParam == CaptionArea) ||
            (message == SystemCommand && ((long)wParam & 0xFFF0) is MoveCommand or SizeCommand))
            handled = true;
        return 0;
    }
}
