namespace GalNet.Game.Controls.Scene;

/// <summary>One stable, backend-agnostic scene draw entry.</summary>
public sealed record SceneRenderEntry(SceneLayerItem Layer, long InsertionOrder)
{
    public double Order => Layer.Z;
}

/// <summary>Ordered Layer input to scene composition. Scene objects join this plan in a later Phase 2 slice.</summary>
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
