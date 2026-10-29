using System.Text.Json;
using GalNet.Core.Runtime;

namespace GeneralTest.Runtime;

public sealed class StateIdentityTests
{
    public sealed record First(string Id);
    public sealed record Second(string Id);
    private static StateCatalog Catalog()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<First>("first", item => item.Id, keySpace: "objects");
        builder.Register<Second>("second", item => item.Id, keySpace: "objects");
        return builder.Build();
    }
    [Test]
    public void SharedKeySpaceRejectsCrossTableCollisionsAndRemovalReleasesTheIdentity()
    {
        var state = new GameState(Catalog());
        state.Table<First>().Add(new("same"));
        Assert.Throws<InvalidOperationException>(() => state.Table<Second>().Add(new("same")));
        var replacement = new First("same"); state.Table<First>().Set(replacement);
        Assert.That(state.TryGetIdentity<First>("objects", "same", out var found), Is.True);
        Assert.That(found, Is.SameAs(replacement));
        state.Table<First>().Clear(); state.Table<Second>().Add(new("same"));
        Assert.That(state.TryGetIdentity<Second>("objects", "same", out _), Is.True);
        Assert.That(state.TryGetIdentity<Second>("other", "same", out _), Is.False);
    }
    [Test]
    public void RestoreRejectsSharedIdentityCollisionsBeforePublishingTheNewRoot()
    {
        var state = new GameState(Catalog()); state.Table<First>().Add(new("same"));
        var captured = state.Capture();
        captured["second"] = new(1, JsonSerializer.SerializeToElement(new[] { new { id = "same" } }));
        Assert.Throws<InvalidOperationException>(() => GameState.Restore(Catalog(), captured));
        Assert.That(state.Table<First>().Get("same").Id, Is.EqualTo("same"));
        Assert.That(state.Table<Second>().Values, Is.Empty);
    }
}
