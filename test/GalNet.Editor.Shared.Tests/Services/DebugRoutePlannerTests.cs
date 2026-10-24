using GalNet.Editor.Shared.Services;
using GalNet.Runtime.Engine;

namespace GalNet.Editor.Shared.Tests.Services;

public class DebugRoutePlannerTests
{
    [Test]
    public void UnorderedSelectedChainIsPreferredWithoutChoosingTheShorterUnselectedRoute()
    {
        GameDebugRouteEdge[] edges = [new("B", 0, "C"), new("C", 0, "D"), new("D", 0, "E"), new("B", 1, "E")];
        var plan = DebugRoutePlanner.Plan("B", "E", edges, ["D", "B", "E", "C"]);
        Assert.That(plan.UsedSelection, Is.True);
        Assert.That(plan.Routes.Single().Select(edge => edge.To), Is.EqualTo(new[] { "C", "D", "E" }));
    }
    [Test]
    public void BrokenSelectionFallsBackToCandidatesAndParallelOutletsRemainDistinct()
    {
        GameDebugRouteEdge[] edges = [new("B", 0, "C"), new("B", 1, "C"), new("C", 0, "E")];
        var plan = DebugRoutePlanner.Plan("B", "E", edges, ["B", "missing", "E"]);
        Assert.That(plan.UsedSelection, Is.False);
        Assert.That(plan.Routes, Has.Length.EqualTo(2));
    }
    [Test]
    public void CyclesAndExponentialCandidatesAreBounded()
    {
        var edges = Enumerable.Range(0, 20).SelectMany(index => new[]
        {
            new GameDebugRouteEdge("n" + index, 0, "n" + (index + 1)),
            new GameDebugRouteEdge("n" + index, 1, "n" + (index + 2)),
            new GameDebugRouteEdge("n" + index, 2, "n0")
        });
        var plan = DebugRoutePlanner.Plan("n0", "n20", edges, maxRoutes: 3, searchBudget: 50);
        Assert.That(plan.Truncated, Is.True);
        Assert.That(plan.Routes.Length, Is.LessThanOrEqualTo(3));
    }
}
