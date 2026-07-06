using Avalonia;

namespace GalNet.Editor.Controls;

/// <summary>
/// 为 MenuItem 提供附加属性，使 Style 选择器可以按 IsSeparator 匹配。
/// </summary>
public class MenuEntry
{
    public static readonly AttachedProperty<bool> IsSeparatorProperty =
        AvaloniaProperty.RegisterAttached<MenuEntry, AvaloniaObject, bool>("IsSeparator");

    public static bool GetIsSeparator(AvaloniaObject obj) => obj.GetValue(IsSeparatorProperty);
    public static void SetIsSeparator(AvaloniaObject obj, bool value) => obj.SetValue(IsSeparatorProperty, value);
}
