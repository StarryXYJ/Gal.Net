using Avalonia;
using Avalonia.Controls;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Layout-owned navigation hints. Targets and positions are discovered from the live page.</summary>
public static class PlayerNavigation
{
    public static readonly AttachedProperty<bool> IsToolbarProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsToolbar", typeof(PlayerNavigation));
    public static readonly AttachedProperty<bool> IsDefaultProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsDefault", typeof(PlayerNavigation));
    public static readonly AttachedProperty<bool> IsTargetProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsTarget", typeof(PlayerNavigation), true);

    public static bool GetIsToolbar(Control control) => control.GetValue(IsToolbarProperty);
    public static void SetIsToolbar(Control control, bool value) => control.SetValue(IsToolbarProperty, value);
    public static bool GetIsDefault(Control control) => control.GetValue(IsDefaultProperty);
    public static void SetIsDefault(Control control, bool value) => control.SetValue(IsDefaultProperty, value);
    /// <summary>Set false on an editor or template part to omit it from gamepad navigation.</summary>
    public static bool GetIsTarget(Control control) => control.GetValue(IsTargetProperty);
    public static void SetIsTarget(Control control, bool value) => control.SetValue(IsTargetProperty, value);
}
