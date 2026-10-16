using Avalonia.Input;

namespace GalNet.Avalonia.GameView.Input;

public static class InputGestureCodec
{
    private static readonly HashSet<string> GamepadCodes = new(StringComparer.Ordinal)
    { "South", "East", "West", "North", "LB", "RB", "LT", "RT", "Start", "Back", "LeftStick", "RightStick", "Up", "Down", "Left", "Right" };
    private static readonly HashSet<string> MouseCodes = new(StringComparer.Ordinal)
    { "MouseLeft", "MouseRight", "MouseMiddle", "MouseX1", "MouseX2", "WheelUp", "WheelDown" };
    public static bool IsValid(InputBinding binding)
    {
        if (binding.Device == PlayerInputDevice.Touch) return PlayerInputSettings.TouchGestures.Contains(binding.Gesture);
        var parts = binding.Gesture.Split('+');
        if (parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace) || parts.Distinct(StringComparer.Ordinal).Count() != parts.Length) return false;
        if (binding.Device == PlayerInputDevice.Gamepad)
            return parts.All(GamepadCodes.Contains) && parts.SequenceEqual(parts.Order(StringComparer.Ordinal));
        var modifiers = KeyModifiers.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var modifier = parts[index] switch { "Control" => KeyModifiers.Control, "Alt" => KeyModifiers.Alt, "Shift" => KeyModifiers.Shift, "Meta" => KeyModifiers.Meta, _ => KeyModifiers.None };
            if (modifier == KeyModifiers.None) return false;
            modifiers |= modifier;
        }
        var last = parts[^1];
        return (last is "Control" or "Alt" or "Shift" or "Meta" || MouseCodes.Contains(last) || Enum.TryParse<Key>(last, out var key) && key != Key.None && Enum.IsDefined(key))
            && Compose(last, modifiers) == binding.Gesture;
    }
    public static string KeyCode(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Control",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.LWin or Key.RWin => "Meta",
        _ => key.ToString()
    };
    public static string Compose(string code, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Control");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Meta");
        if (!parts.Contains(code)) parts.Add(code);
        return string.Join('+', parts);
    }
    public static string? MouseCode(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => "MouseLeft",
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => "MouseRight",
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => "MouseMiddle",
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => "MouseX1",
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => "MouseX2",
        _ => null
    };
}
