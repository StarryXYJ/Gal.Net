using Avalonia;
using Avalonia.Controls;

namespace GalNet.Editor.Themes;

/// <summary>Separates icon surfaces from the neutral native-button palette.</summary>
public sealed class EditorButton : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsIconProperty = AvaloniaProperty.RegisterAttached<EditorButton, Button, bool>("IsIcon");
    static EditorButton() => IsIconProperty.Changed.AddClassHandler<Button>((button, args) => button.Classes.Set("editor-icon", args.NewValue is true));
    public static bool GetIsIcon(Button button) => button.GetValue(IsIconProperty);
    public static void SetIsIcon(Button button, bool value) => button.SetValue(IsIconProperty, value);
}
