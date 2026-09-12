namespace GalNet.Core.Scene;

/// <summary>Describes the used frames in a row-major sprite sheet. Frame indices are zero-based.</summary>
public sealed class FlipbookDefinition
{
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public int FrameCount { get; set; } = 1;
    public float Index { get; set; }

    public int Capacity => Columns * Rows;

    public bool IsValid => Columns > 0 && Rows > 0 && FrameCount is > 0 && FrameCount <= Capacity;

    public int CurrentFrameIndex => Math.Clamp((int)MathF.Floor(Index), 0, Math.Max(0, FrameCount - 1));

    public FlipbookDefinition Clone() => new() { Columns = Columns, Rows = Rows, FrameCount = FrameCount, Index = Index };
}

/// <summary>The current source region selected by a Layer, independent of whether its source is static or a sprite sheet.</summary>
public readonly record struct LayerFrame(string AssetId, int Index, int Columns, int Rows)
{
    public bool IsFlipbook => Columns > 1 || Rows > 1;
    public int Column => Index % Columns;
    public int Row => Index / Columns;
}
