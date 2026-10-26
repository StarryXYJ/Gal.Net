using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GalNet.IntegrationTests;

internal static class NativeSampleWindow
{
    public static void Close(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(process.CloseMainWindow(), Is.True);
            return;
        }

        // Process.MainWindowHandle can select an untitled native helper window.
        var windows = new List<nint>();
        Assert.That(EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != process.Id || !IsWindowVisible(window)) return true;
            var title = new StringBuilder(512);
            GetWindowText(window, title, title.Capacity);
            TestContext.Out.WriteLine($"Process {owner} window {window}: '{title}'.");
            if (title.ToString().StartsWith("GalNet Avalonia", StringComparison.Ordinal)) windows.Add(window);
            return true;
        }, 0), Is.True);
        Assert.That(windows, Has.Count.EqualTo(1), "Expected the Sample's visible titled window in its own process.");
        Assert.That(PostMessage(windows.Single(), 0x0010, 0, 0), Is.True, "The Sample window must receive WM_CLOSE.");
    }

    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder title, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
