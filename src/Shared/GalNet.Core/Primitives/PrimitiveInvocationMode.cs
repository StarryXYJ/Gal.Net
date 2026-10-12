namespace GalNet.Core.Primitives;

public enum PrimitiveInvocationMode { Normal, Quick, CatchUp }
public enum PlaybackMode { Manual, Quick, Auto }

/// <summary>A reading primitive whose presentation and player advance are separate phases.</summary>
public interface IAdvanceWaitInstance
{
    bool IsWaitingForAdvance { get; }
    bool IsTextPresented { get; }
    void CompleteAdvance();
}
