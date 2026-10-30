namespace GalNet.Avalonia.Debugging;

/// <summary>Deliberately reduced save forms intended for repeatable debug testing.</summary>
public enum DebugSaveKind
{
    ProgressOnly,
    GalGroupStart
}

/// <summary>Result of attempting to write one of the visible debug save slots.</summary>
public sealed record DebugSaveResult(int? SlotIndex, bool RequiresOverwriteConfirmation)
{
    public static DebugSaveResult Saved(int slotIndex) => new(slotIndex, false);
    public static DebugSaveResult RequiresOverwrite() => new(null, true);
}
