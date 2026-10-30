using System.Runtime.InteropServices;

namespace GalNet.Probes.Sdl3;

// Deliberately narrow probe binding. SDL bool is one byte; enum/int is four.
// SDL_VirtualJoystickDesc is a size-versioned interface, not SDL2's descriptor.
internal static partial class SdlNative
{
    private const string Library = "galnet-sdl3-probe";
    internal const uint GamepadSubsystem = 0x2000;
    internal const uint VideoSubsystem = 0x20;
    internal const uint AudioSubsystem = 0x10;

    [LibraryImport(Library, EntryPoint = "SDL_InitSubSystem")]
    internal static partial byte Init(uint flags);
    [LibraryImport(Library, EntryPoint = "SDL_QuitSubSystem")]
    internal static partial void Quit(uint flags);
    [LibraryImport(Library, EntryPoint = "SDL_WasInit")]
    internal static partial uint WasInit(uint flags);
    [LibraryImport(Library, EntryPoint = "SDL_GetVersion")]
    internal static partial int GetVersion();
    [LibraryImport(Library, EntryPoint = "SDL_IsMainThread")]
    internal static partial byte IsMainThread();
    [LibraryImport(Library, EntryPoint = "SDL_GetError")]
    private static partial nint GetError();
    internal static string Error => Marshal.PtrToStringUTF8(GetError()) ?? "Unknown SDL error";
    [LibraryImport(Library, EntryPoint = "SDL_SetHint", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial byte SetHint(string name, string value);
    [LibraryImport(Library, EntryPoint = "SDL_PumpEvents")]
    internal static partial void PumpEvents();
    [LibraryImport(Library, EntryPoint = "SDL_FlushEvents")]
    internal static partial void FlushEvents(uint min, uint max);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepads")]
    private static partial nint GetGamepads(out int count);
    [LibraryImport(Library, EntryPoint = "SDL_free")]
    private static partial void Free(nint memory);

    internal static unsafe uint[] Gamepads()
    {
        var memory = GetGamepads(out var count);
        if (memory == 0) throw new InvalidOperationException(Error);
        try { return new ReadOnlySpan<uint>((void*)memory, count).ToArray(); }
        finally { Free(memory); }
    }

    [LibraryImport(Library, EntryPoint = "SDL_AttachVirtualJoystick")]
    internal static partial uint AttachVirtualJoystick(in VirtualJoystickDesc desc);
    [LibraryImport(Library, EntryPoint = "SDL_DetachVirtualJoystick")]
    internal static partial byte DetachVirtualJoystick(uint id);
    [LibraryImport(Library, EntryPoint = "SDL_OpenJoystick")]
    internal static partial nint OpenJoystick(uint id);
    [LibraryImport(Library, EntryPoint = "SDL_CloseJoystick")]
    internal static partial void CloseJoystick(nint joystick);
    [LibraryImport(Library, EntryPoint = "SDL_SetJoystickVirtualButton")]
    internal static partial byte SetVirtualButton(nint joystick, int button, byte down);
    [LibraryImport(Library, EntryPoint = "SDL_SetJoystickVirtualAxis")]
    internal static partial byte SetVirtualAxis(nint joystick, int axis, short value);
    [LibraryImport(Library, EntryPoint = "SDL_OpenGamepad")]
    internal static partial nint OpenGamepad(uint id);
    [LibraryImport(Library, EntryPoint = "SDL_CloseGamepad")]
    internal static partial void CloseGamepad(nint gamepad);
    [LibraryImport(Library, EntryPoint = "SDL_GamepadConnected")]
    internal static partial byte GamepadConnected(nint gamepad);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadButton")]
    internal static partial byte GetButton(nint gamepad, int button);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadAxis")]
    internal static partial short GetAxis(nint gamepad, int axis);

    // The official 3.4.18 header asserts 136 bytes for 64-bit targets.
    [StructLayout(LayoutKind.Sequential)]
    internal struct VirtualJoystickDesc
    {
        internal uint Version;
        internal ushort Type, Padding, VendorId, ProductId, AxisCount, ButtonCount;
        internal ushort BallCount, HatCount, TouchpadCount, SensorCount, Padding2A, Padding2B;
        internal uint ButtonMask, AxisMask;
        internal nint Name, Touchpads, Sensors, UserData;
        internal nint Update, SetPlayerIndex, Rumble, RumbleTriggers;
        internal nint SetLed, SendEffect, SetSensorsEnabled, Cleanup;
    }
}
