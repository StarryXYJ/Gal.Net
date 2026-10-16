using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Input;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Windows XInput adapter. Neutral button codes are the only values crossing into GameView.</summary>
internal sealed class SampleGamepadInput : IDisposable
{
    private readonly PlayerInputSettings _input;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(32) };
    private Window? _window;
    private HashSet<string> _previous = [];
    private bool _available = OperatingSystem.IsWindows();
    public SampleGamepadInput(PlayerInputSettings input)
    {
        _input = input;
        input.GamepadSupported = _available;
        _timer.Tick += Tick;
    }
    public void Bind(Window? window) { _window = window; if (window is null) _timer.Stop(); else _timer.Start(); }
    private void Tick(object? sender, EventArgs e)
    {
        var buttons = new HashSet<string>(StringComparer.Ordinal);
        var connected = false;
        if (_available && _window?.IsActive == true)
        {
            try
            {
                for (uint index = 0; index < 4; index++)
                {
                    if (XInputGetState(index, out var state) != 0) continue;
                    connected = true;
                    var gamepad = state.Gamepad;
                    foreach (var (mask, name) in ButtonCodes)
                        if ((gamepad.Buttons & mask) != 0) buttons.Add(name);
                    if (gamepad.LeftTrigger > 128) buttons.Add("LT");
                    if (gamepad.RightTrigger > 128) buttons.Add("RT");
                    if (gamepad.ThumbLX > 16000) buttons.Add("Right");
                    if (gamepad.ThumbLX < -16000) buttons.Add("Left");
                    if (gamepad.ThumbLY > 16000) buttons.Add("Up");
                    if (gamepad.ThumbLY < -16000) buttons.Add("Down");
                    break;
                }
            }
            catch (DllNotFoundException) { _available = false; _input.GamepadSupported = false; }
            catch (EntryPointNotFoundException) { _available = false; _input.GamepadSupported = false; }
        }
        if (!connected)
        {
            if (_previous.Count > 0) { _previous = []; _input.CancelGamepad(); }
            return;
        }
        if (_previous.SetEquals(buttons)) return;
        _previous = buttons;
        _input.ObserveGamepad(buttons);
    }
    private static readonly (ushort Mask, string Name)[] ButtonCodes =
    [ (1, "Up"), (2, "Down"), (4, "Left"), (8, "Right"), (16, "Start"), (32, "Back"),
      (64, "LeftStick"), (128, "RightStick"), (256, "LB"), (512, "RB"),
      (4096, "South"), (8192, "East"), (16384, "West"), (32768, "North") ];
    [StructLayout(LayoutKind.Sequential)]
    private struct XGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct XState { public uint Packet; public XGamepad Gamepad; }
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint index, out XState state);
    public void Dispose() { _timer.Stop(); _timer.Tick -= Tick; _input.CancelGamepad(); }
}
