namespace GalNet.Rendering.Scene;

public sealed record SceneRenderEntry(ISceneRenderable Renderable, long InsertionOrder)
{
    public double Order => Renderable.Z;
}

/// <summary>Stable composition order shared by every scene backend.</summary>
public sealed class SceneRenderPlan
{
    public static SceneRenderPlan Empty { get; } = new([]);
    public IReadOnlyList<SceneRenderEntry> Items { get; }
    private SceneRenderPlan(IReadOnlyList<SceneRenderEntry> items) => Items = items;
    public static SceneRenderPlan Create(IEnumerable<SceneRenderEntry> entries) => new(entries
        .OrderBy(entry => entry.Order)
        .ThenBy(entry => entry.InsertionOrder)
        .ToArray());
}
