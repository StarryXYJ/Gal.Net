using GalNet.Presentation.Abstractions.Input;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Focus, connection and neutral-position gate for a scope's player actions.
/// Selection stays with the first connected device until it is removed.</summary>
public sealed class GamepadInputSession(PlayerInputSettings input)
{
    private string? _selected;
    private bool _focused, _armed;
    private HashSet<string> _previous = [];
    private readonly HashSet<string> _axes = [];
    private static readonly (StandardGamepadButtons Flag, string Code)[] Codes =
    [
        (StandardGamepadButtons.South, "South"), (StandardGamepadButtons.East, "East"),
        (StandardGamepadButtons.West, "West"), (StandardGamepadButtons.North, "North"),
        (StandardGamepadButtons.Back, "Back"), (StandardGamepadButtons.Start, "Start"),
        (StandardGamepadButtons.LeftStick, "LeftStick"), (StandardGamepadButtons.RightStick, "RightStick"),
        (StandardGamepadButtons.LeftShoulder, "LB"), (StandardGamepadButtons.RightShoulder, "RB"),
        (StandardGamepadButtons.Up, "Up"), (StandardGamepadButtons.Down, "Down"),
        (StandardGamepadButtons.Left, "Left"), (StandardGamepadButtons.Right, "Right")
    ];

    public void Update(IReadOnlyList<StandardGamepadSnapshot> devices, bool focused)
    {
        var device = devices.FirstOrDefault(device => device.Id == _selected) ?? devices.FirstOrDefault();
        if (device?.Id != _selected)
        {
            Cancel();
            _selected = device?.Id;
        }
        input.SetGamepadDevice(device?.Name, device?.Layout ?? GamepadLayout.Unknown, device?.Labels ?? GamepadFaceLabels.Unknown);
        if (!focused || device is null)
        {
            if (_focused) Cancel();
            _focused = false;
            return;
        }
        if (!_focused) { _armed = false; _axes.Clear(); }
        _focused = true;
        var buttons = Map(device);
        if (!_armed)
        {
            // Reconnection/focus return must see a neutral sample before any
            // held input can become an action or a new capture draft.
            if (buttons.Count == 0 && IsNeutral(device)) _armed = true;
            return;
        }
        if (_previous.SetEquals(buttons)) return;
        _previous = buttons;
        input.ObserveGamepad(buttons);
    }

    private HashSet<string> Map(StandardGamepadSnapshot device)
    {
        HashSet<string> buttons = [];
        foreach (var (flag, code) in Codes)
            if ((device.Buttons & flag) != 0) buttons.Add(code);
        Axis("Left", -device.LeftX, .5f, .35f);
        Axis("Right", device.LeftX, .5f, .35f);
        Axis("Up", -device.LeftY, .5f, .35f);
        Axis("Down", device.LeftY, .5f, .35f);
        Axis("LT", device.LeftTrigger, .5f, .4f);
        Axis("RT", device.RightTrigger, .5f, .4f);
        buttons.UnionWith(_axes);
        return buttons;
    }

    private void Axis(string code, float value, float press, float release)
    {
        if (!float.IsFinite(value) || value <= release) _axes.Remove(code);
        else if (value >= press) _axes.Add(code);
    }

    private static bool IsNeutral(StandardGamepadSnapshot device) =>
        float.IsFinite(device.LeftX) && float.IsFinite(device.LeftY) &&
        float.IsFinite(device.LeftTrigger) && float.IsFinite(device.RightTrigger) &&
        Math.Abs(device.LeftX) <= .35f && Math.Abs(device.LeftY) <= .35f &&
        device.LeftTrigger <= .4f && device.RightTrigger <= .4f;

    public void Cancel()
    {
        _armed = false;
        _focused = false;
        _previous = [];
        _axes.Clear();
        input.CancelGamepad();
    }

    public void Reset()
    {
        Cancel();
        _selected = null;
        input.SetGamepadDevice(null, GamepadLayout.Unknown, GamepadFaceLabels.Unknown);
    }
}
