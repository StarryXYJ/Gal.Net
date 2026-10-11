namespace GalNet.Runtime.Engine;

public enum GameEngineState
{
    Ready,
    Running,
    Completed,
    Faulted,
    Stopped
}

/// <summary>An ordered engine state notification; UI and persistence decisions belong to the host.</summary>
public sealed record GameEngineStateChange(GameEngineState State, Exception? Error = null);
