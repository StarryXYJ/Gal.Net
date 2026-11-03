namespace GalNet.Debugging.Tests;

public sealed class DebugContentImpactClassifierTests
{
    [Test]
    public void FutureGroupAndCurrentSuffixAreDirect()
    {
        var current = Manifest(Group("current", Entry("a", 0), Entry("b", 1)), Group("future", Entry("x", 0)));
        var candidate = Manifest(Group("current", Entry("a", 0), Entry("b", 1, "changed")), Group("future", Entry("x", 0, "changed")))
            with { ContentFingerprint = "next" };

        var result = DebugContentImpactClassifier.Classify(current, candidate, Position(entryIndex: 1));

        Assert.That(result, Is.EqualTo(DebugContentImpact.Direct));
    }

    [Test]
    public void ExecutedPrefixChangeRequiresGroupStart()
    {
        var current = Manifest(Group("current", Entry("a", 0), Entry("b", 1)));
        var candidate = Manifest(Group("current", Entry("a", 0, "changed"), Entry("b", 1))) with { ContentFingerprint = "next" };

        var result = DebugContentImpactClassifier.Classify(current, candidate, Position(entryIndex: 1));

        Assert.That(result, Is.EqualTo(DebugContentImpact.ReturnGroupStart));
    }

    [Test]
    public void ActiveCompositeCountsAsExecutedEvenBeforeItsSpanEnds()
    {
        var current = Manifest(Group("current", Entry("a", 0, primitives: ["a#1", "a#2"]), Entry("b", 2)));
        var candidate = Manifest(Group("current", Entry("a", 0, "changed", ["a#1", "a#2"]), Entry("b", 2))) with { ContentFingerprint = "next" };

        var result = DebugContentImpactClassifier.Classify(current, candidate,
            Position(entryIndex: 1, active: ["a#1"]));

        Assert.That(result, Is.EqualTo(DebugContentImpact.ReturnGroupStart));
    }

    [Test]
    public void DirectChangeMapsTheExecutedAuthorBoundaryIntoCandidateSpans()
    {
        var current = Manifest(Group("current", Entry("a", 0, primitives: ["a#1"]), Entry("b", 1)));
        var candidate = Manifest(Group("current", Entry("a", 0, primitives: ["a#1"]),
            Entry("inserted", 1, primitives: ["i#1", "i#2"]), Entry("b", 3, "changed"))) with { ContentFingerprint = "next" };

        var mapped = DebugContentImpactClassifier.MapCandidateEntryIndex(current, candidate, Position(entryIndex: 1));

        Assert.That(mapped, Is.EqualTo(1));
    }

    [Test]
    public void VisitedGroupChangeRequiresRestart()
    {
        var current = Manifest(Group("past", Entry("a", 0)), Group("current", Entry("b", 0)));
        var candidate = Manifest(Group("past", Entry("a", 0, "changed")), Group("current", Entry("b", 0))) with { ContentFingerprint = "next" };

        var result = DebugContentImpactClassifier.Classify(current, candidate,
            Position(entryIndex: 0, visited: ["past", "current"]));

        Assert.That(result, Is.EqualTo(DebugContentImpact.RestartGame));
    }

    [Test]
    public void SessionFileChangeRequiresRestart()
    {
        var current = Manifest(Group("current", Entry("a", 0))) with
        { Files = [new("I18n/zh-CN/text.json", "OLD", true)] };
        var candidate = Manifest(Group("current", Entry("a", 0))) with
        { ContentFingerprint = "next", Files = [new("I18n/zh-CN/text.json", "NEW", true)] };

        var result = DebugContentImpactClassifier.Classify(current, candidate, Position(entryIndex: 0));

        Assert.That(result, Is.EqualTo(DebugContentImpact.RestartGame));
    }

    [Test]
    public void ContractChangeRequiresApplicationRestart()
    {
        var current = Manifest(Group("current", Entry("a", 0)));
        var candidate = Manifest(Group("current", Entry("a", 0))) with { ContentFingerprint = "next", ContractFingerprint = "other" };

        Assert.Throws<InvalidOperationException>(() =>
            DebugContentImpactClassifier.Classify(current, candidate, Position(entryIndex: 0)));
    }

    private static DebugContentPosition Position(int entryIndex, string[]? active = null, string[]? visited = null) =>
        new("current", entryIndex, active ?? [], visited ?? ["current"]);

    private static DebugContentManifest Manifest(params DebugContentGroup[] groups) =>
        new(DebugContentManifest.CurrentVersion, "project", "content", "contract", "author", "graph", groups, []);

    private static DebugContentGroup Group(string id, params DebugContentEntry[] entries) => new(id, entries);

    private static DebugContentEntry Entry(string id, int start, string hash = "same", string[]? primitives = null)
    {
        primitives ??= [$"{id}#1"];
        return new(id, start, start + primitives.Length, hash, primitives);
    }
}
