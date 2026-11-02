using Avalonia;
using Avalonia.Controls;

namespace GalNet.Sample.CrossPlatform;

/// <summary>Project-owned platform layout flag; small desktop windows retain their desktop composition.</summary>
public sealed class PlayerLayout
{
    private PlayerLayout() { }
    public static readonly AttachedProperty<bool> IsMobileProperty = AvaloniaProperty.RegisterAttached<PlayerLayout, Control, bool>(
        "IsMobile", OperatingSystem.IsAndroid() || OperatingSystem.IsIOS(), inherits: true);
    public static bool GetIsMobile(Control control) => control.GetValue(IsMobileProperty);
    public static void SetIsMobile(Control control, bool value) => control.SetValue(IsMobileProperty, value);
}
