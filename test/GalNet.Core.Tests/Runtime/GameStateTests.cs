using System.Text.Json;
using GalNet.Core.Runtime;

namespace GeneralTest.Runtime;

public class GameStateTests
{
    public sealed class CustomState
    {
        public string Id { get; init; } = "";
        public List<int> Values { get; init; } = [];
    }
    private static StateCatalog Catalog()
    {
        var builder = new StateCatalogBuilder();
        builder.Register<CustomState>("project.custom", value => value.Id);
        return builder.Build();
    }
    [Test]
    public void CustomTypeRoundTripsWithoutCentralCodeAndOwnsNestedData()
    {
        var catalog = Catalog(); var state = new GameState(catalog);
        state.Table<CustomState>().Add(new CustomState { Id = "one", Values = [1, 2] });
        var snapshot = state.Capture();
        state.Table<CustomState>().Get("one").Values.Add(3);
        var restored = GameState.Restore(catalog, snapshot);
        Assert.That(restored.Table<CustomState>().Get("one").Values, Is.EqualTo(new[] { 1, 2 }));
        restored.Table<CustomState>().Get("one").Values.Clear();
        Assert.That(GameState.Restore(catalog, snapshot).Table<CustomState>().Get("one").Values, Has.Count.EqualTo(2));
    }
    [TestCase("missing")]
    [TestCase("unknown")]
    [TestCase("version")]
    [TestCase("duplicate")]
    [TestCase("payload")]
    public void InvalidSectionsAreRejected(string fault)
    {
        var catalog = Catalog(); var sections = new GameState(catalog).Capture();
        switch (fault)
        {
            case "missing": sections.Clear(); break;
            case "unknown": sections["other"] = sections["project.custom"]; break;
            case "version": sections["project.custom"] = new(2, JsonSerializer.SerializeToElement(Array.Empty<object>())); break;
            case "duplicate": sections["project.custom"] = new(1, JsonSerializer.SerializeToElement(new[] { new { id = "same" }, new { id = "same" } })); break;
            case "payload": sections["project.custom"] = new(1, JsonSerializer.SerializeToElement(new { })); break;
        }
        Assert.Throws<InvalidDataException>(() => GameState.Restore(catalog, sections));
    }
    [Test]
    public void ContractIdsAndTypesAreBothExclusive()
    {
        var builder = new StateCatalogBuilder(); builder.Register<CustomState>("one", value => value.Id);
        Assert.Throws<InvalidOperationException>(() => builder.Register<CustomState>("two", value => value.Id));
        Assert.Throws<InvalidOperationException>(() => builder.Register<string>("one", value => value));
    }
}
