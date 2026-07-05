namespace GalNet.Core.Services;

/// <summary>
/// Keyboard shortcut combination.
/// </summary>
public sealed class KeyGesture
{
    public string Key { get; }
    public bool Ctrl { get; }
    public bool Alt { get; }
    public bool Shift { get; }

    public KeyGesture(string key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        Key = key;
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
    }

    public override string ToString()
    {
        var mods = new List<string>();
        if (Ctrl) mods.Add("Ctrl");
        if (Alt) mods.Add("Alt");
        if (Shift) mods.Add("Shift");
        mods.Add(Key);
        return string.Join("+", mods);
    }
}

/// <summary>
/// Input/hotkey registration service.
/// </summary>
public interface IInputService
{
    /// <summary>Register a global hotkey gesture. Returns a disposable to unregister.</summary>
    IDisposable RegisterHotkey(KeyGesture gesture, Action handler);
}
