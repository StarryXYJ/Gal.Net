using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GalNet.Input.Sdl3;

internal static partial class SdlNative
{
    internal const uint Gamepad = 0x2000, Joystick = 0x200;
    internal const int PinnedVersion = 3_004_018;
    private const string Library = "galnet-private-sdl3";
    private static readonly Lazy<nint> Handle = new(LoadPrivateLibrary);
    internal static string? LoadedPath { get; private set; }

    static SdlNative() => NativeLibrary.SetDllImportResolver(typeof(SdlNative).Assembly,
        (name, _, _) => name == Library ? Handle.Value : 0);

    private static nint LoadPrivateLibrary()
    {
        var filename = OperatingSystem.IsWindows() ? "SDL3.dll" :
            OperatingSystem.IsMacOS() ? "libSDL3.0.dylib" : "libSDL3.so.0";
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string[] directories = [AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "runtimes", $"{platform}-{architecture}", "native")];
        foreach (var directory in directories)
        {
            var path = Path.Combine(directory, filename);
            if (!File.Exists(path)) continue;
            var handle = NativeLibrary.Load(path);
            LoadedPath = path;
            return handle;
        }
        throw new DllNotFoundException($"Private SDL 3.4.18 is missing: {filename} ({platform}-{architecture}).");
    }

    [LibraryImport(Library, EntryPoint = "SDL_GetVersion")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetVersion();
    [LibraryImport(Library, EntryPoint = "SDL_InitSubSystem")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte Init(uint flags);
    [LibraryImport(Library, EntryPoint = "SDL_QuitSubSystem")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Quit(uint flags);
    [LibraryImport(Library, EntryPoint = "SDL_WasInit")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint WasInit(uint flags);
    [LibraryImport(Library, EntryPoint = "SDL_IsMainThread")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte IsMainThread();
    [LibraryImport(Library, EntryPoint = "SDL_GetError")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint GetError();
    internal static string Error => Marshal.PtrToStringUTF8(GetError()) ?? "Unknown SDL error";
    [LibraryImport(Library, EntryPoint = "SDL_GetHint", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint GetHintNative(string name);
    internal static string? GetHint(string name) => Marshal.PtrToStringUTF8(GetHintNative(name));
    [LibraryImport(Library, EntryPoint = "SDL_SetHint", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte SetHint(string name, string value);
    [LibraryImport(Library, EntryPoint = "SDL_ResetHint", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte ResetHint(string name);
    [LibraryImport(Library, EntryPoint = "SDL_PumpEvents")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void PumpEvents();
    [LibraryImport(Library, EntryPoint = "SDL_UpdateGamepads")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void UpdateGamepads();
    [LibraryImport(Library, EntryPoint = "SDL_FlushEvents")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void FlushEvents(uint min, uint max);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepads")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint GetGamepads(out int count);
    [LibraryImport(Library, EntryPoint = "SDL_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Free(nint memory);
    [LibraryImport(Library, EntryPoint = "SDL_OpenGamepad")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint OpenGamepad(uint id);
    [LibraryImport(Library, EntryPoint = "SDL_CloseGamepad")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void CloseGamepad(nint gamepad);
    [LibraryImport(Library, EntryPoint = "SDL_GamepadConnected")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte Connected(nint gamepad);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadButton")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial byte GetButton(nint gamepad, int button);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadAxis")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial short GetAxis(nint gamepad, int axis);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadName")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint GetName(nint gamepad);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadType")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetType(nint gamepad);
    [LibraryImport(Library, EntryPoint = "SDL_GetGamepadButtonLabel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int GetLabel(nint gamepad, int button);
}
