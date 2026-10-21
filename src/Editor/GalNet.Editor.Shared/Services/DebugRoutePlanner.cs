using GalNet.Runtime.Engine;

namespace GalNet.Editor.Shared.Services;

public sealed record DebugRoutePlan(GameDebugRouteEdge[][] Routes, bool UsedSelection, bool Truncated);

/// <summary>Bounded topology-only planning. Conditions belong to execution policy, never route discovery.</summary>
public static class DebugRoutePlanner
{
    public static DebugRoutePlan Plan(string start, string target, IEnumerable<GameDebugRouteEdge> graph,
        IReadOnlyCollection<string>? selected = null, int maxRoutes = 24, int maxDepth = 64, int searchBudget = 4096)
    {
        if (maxRoutes < 1 || maxDepth < 1 || searchBudget < 1) throw new ArgumentOutOfRangeException(nameof(maxRoutes));
        var outgoing = graph.GroupBy(edge => edge.From).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var selection = selected?.ToHashSet(StringComparer.Ordinal) ?? [];
        if (selection.Count > 1 && selection.Contains(start) && selection.Contains(target))
        {
            var candidates = new List<GameDebugRouteEdge[]>();
            var pending = new Stack<(string Node, GameDebugRouteEdge[] Path, HashSet<string> Visited)>();
            pending.Push((start, [], new(StringComparer.Ordinal) { start }));
            var budget = searchBudget;
            while (pending.TryPop(out var work) && --budget >= 0 && candidates.Count < 2)
            {
                if (work.Node == target)
                {
                    if (work.Visited.SetEquals(selection)) candidates.Add(work.Path);
                    continue;
                }
                if (work.Path.Length >= maxDepth) continue;
                foreach (var edge in outgoing.GetValueOrDefault(work.Node, []))
                    if (selection.Contains(edge.To) && !work.Visited.Contains(edge.To) && pending.Count < searchBudget)
                        pending.Push((edge.To, [.. work.Path, edge], new(work.Visited, StringComparer.Ordinal) { edge.To }));
            }
            if (candidates.Count == 1 && pending.Count == 0 && budget >= 0) return new([candidates[0]], true, false);
        }
        var routes = new List<GameDebugRouteEdge[]>();
        var queue = new Queue<(string Node, GameDebugRouteEdge[] Path, HashSet<string> Visited)>();
        queue.Enqueue((start, [], new(StringComparer.Ordinal) { start }));
        var truncated = false;
        while (queue.TryDequeue(out var work))
        {
            if (--searchBudget < 0) { truncated = true; break; }
            if (work.Node == target)
            {
                if (routes.Count == maxRoutes) { truncated = true; break; }
                routes.Add(work.Path); continue;
            }
            if (work.Path.Length >= maxDepth) { truncated = true; continue; }
            foreach (var edge in outgoing.GetValueOrDefault(work.Node, []))
            {
                if (work.Visited.Contains(edge.To)) continue;
                if (queue.Count >= searchBudget) { truncated = true; break; }
                queue.Enqueue((edge.To, [.. work.Path, edge], new(work.Visited, StringComparer.Ordinal) { edge.To }));
            }
        }
        return new(routes.ToArray(), false, truncated);
    }
}
