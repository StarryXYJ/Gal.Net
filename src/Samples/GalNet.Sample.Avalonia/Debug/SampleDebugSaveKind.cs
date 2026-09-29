namespace GalNet.Sample.Avalonia.Debug;

/// <summary>Deliberately reduced save forms intended for repeatable debug testing.</summary>
internal enum SampleDebugSaveKind
{
    ProgressOnly,
    GalGroupStart
}

/// <summary>Result of attempting to write one of the visible debug save slots.</summary>
internal sealed record SampleDebugSaveResult(int? SlotIndex, bool RequiresOverwriteConfirmation)
{
    public static SampleDebugSaveResult Saved(int slotIndex) => new(slotIndex, false);
    public static SampleDebugSaveResult RequiresOverwrite() => new(null, true);
}
