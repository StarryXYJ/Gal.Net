namespace GalNet.Presentation.Abstractions.Input;

[Flags]
public enum StandardGamepadButtons
{
    None = 0, South = 1, East = 2, West = 4, North = 8,
    Back = 16, Guide = 32, Start = 64, LeftStick = 128, RightStick = 256,
    LeftShoulder = 512, RightShoulder = 1024, Up = 2048, Down = 4096, Left = 8192, Right = 16384
}

public enum GamepadLayout { Unknown, Standard, Xbox, PlayStation, Nintendo, GameCube, Steam }
public enum GamepadButtonLabel { Unknown, A, B, X, Y, Cross, Circle, Square, Triangle }
public sealed record GamepadFaceLabels(GamepadButtonLabel South, GamepadButtonLabel East,
    GamepadButtonLabel West, GamepadButtonLabel North)
{
    public static GamepadFaceLabels Unknown { get; } = new(GamepadButtonLabel.Unknown, GamepadButtonLabel.Unknown,
        GamepadButtonLabel.Unknown, GamepadButtonLabel.Unknown);
}

/// <summary>Device positions and normalized axes, independent of story state and native handles.
/// Stick Y increases downward; triggers range from zero to one. Id is unique for this connection.</summary>
public sealed record StandardGamepadSnapshot(string Id, string Name, GamepadLayout Layout, GamepadFaceLabels Labels,
    StandardGamepadButtons Buttons, float LeftX = 0, float LeftY = 0, float RightX = 0, float RightY = 0,
    float LeftTrigger = 0, float RightTrigger = 0);

/// <summary>Host-owned source. Create, poll and dispose on the initializing UI/main thread.</summary>
public interface IStandardGamepadSource : IDisposable
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    IReadOnlyList<StandardGamepadSnapshot> Poll();
}
