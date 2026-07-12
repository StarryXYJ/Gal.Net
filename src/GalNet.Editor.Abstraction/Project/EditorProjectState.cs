namespace GalNet.Editor.Abstraction.Project;

public sealed class EditorProjectState
{
    public GraphViewportState GraphViewport { get; set; } = new();
}

public sealed class GraphViewportState
{
    public double Zoom { get; set; } = 1.0;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
}