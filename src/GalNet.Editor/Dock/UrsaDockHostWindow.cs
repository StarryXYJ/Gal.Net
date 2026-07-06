using System;
using Avalonia.Controls;
using Dock.Avalonia.Controls;

namespace GalNet.Editor.Dock;

/// <summary>
/// Dock floating host that keeps Dock.Avalonia's native docking behavior while using app window styling.
/// </summary>
public sealed class UrsaDockHostWindow : HostWindow
{
    protected override Type StyleKeyOverride => typeof(UrsaDockHostWindow);

    public UrsaDockHostWindow()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
    }
}
