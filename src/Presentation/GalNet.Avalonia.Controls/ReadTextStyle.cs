using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GalNet.Game.Controls;

/// <summary>Inherited defaults for previously read typewriter text. Explicit rich-text colors win.</summary>
public sealed class ReadTextStyle : AvaloniaObject
{
    public static readonly AttachedProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.RegisterAttached<ReadTextStyle, Control, IBrush?>("Foreground", inherits: true);
    public static readonly AttachedProperty<bool> WasReadProperty =
        AvaloniaProperty.RegisterAttached<ReadTextStyle, Control, bool>("WasRead", inherits: true);
    public static IBrush? GetForeground(Control control) => control.GetValue(ForegroundProperty);
    public static void SetForeground(Control control, IBrush? value) => control.SetValue(ForegroundProperty, value);
    public static bool GetWasRead(Control control) => control.GetValue(WasReadProperty);
    public static void SetWasRead(Control control, bool value) => control.SetValue(WasReadProperty, value);
}
