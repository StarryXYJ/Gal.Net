using GalNet.Core.Entry;

namespace GeneralTest.Entry;

public class EntryModelTests
{
    [Test]
    public void SimpleEntry_Should_Store_Data()
    {
        var entry = new SimpleEntry
        {
            Id = "5_1",
            SourceId = 5,
            Type = "show_layer",
            Condition = "flag == true",
            Params = new Dictionary<string, string>
            {
                ["id"] = "alice",
                ["asset"] = "alice_smile"
            }
        };

        Assert.That(entry.Id, Is.EqualTo("5_1"));
        Assert.That(entry.SourceId, Is.EqualTo(5));
        Assert.That(entry.Type, Is.EqualTo("show_layer"));
        Assert.That(entry.Condition, Is.EqualTo("flag == true"));
        Assert.That(entry.Params["id"], Is.EqualTo("alice"));
    }

    [Test]
    public void SimpleEntry_Should_Have_Empty_Condition_By_Default()
    {
        var entry = new SimpleEntry { Id = "1", Type = "narration" };
        Assert.That(entry.Condition, Is.Empty);
    }
}

/// <summary>
/// 模拟复杂条目编译——验证 ID 方案。
/// </summary>
public sealed class MockComplexEntry : ComplexEntry
{
    private readonly int _subEntryCount;

    public MockComplexEntry(int id, string type, int subEntryCount = 1)
    {
        Id = id;
        Type = type;
        _subEntryCount = subEntryCount;
    }

    public override IReadOnlyList<SimpleEntry> Compile()
    {
        var result = new List<SimpleEntry>();
        for (var i = 1; i <= _subEntryCount; i++)
        {
            result.Add(new SimpleEntry
            {
                Id = _subEntryCount == 1 ? $"{Id}" : $"{Id}_{i}",
                SourceId = Id,
                Type = Type,
                Condition = Condition,
                Params = new Dictionary<string, string>(Params)
            });
        }
        return result;
    }
}

public class CompileIdSchemeTests
{
    [Test]
    public void Compile_SingleEntry_Should_Use_Raw_Id()
    {
        var complex = new MockComplexEntry(5, "narration");
        var result = complex.Compile();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("5"));
        Assert.That(result[0].SourceId, Is.EqualTo(5));
    }

    [Test]
    public void Compile_MultipleEntries_Should_Use_Underscore_Suffix()
    {
        var complex = new MockComplexEntry(5, "show_character", subEntryCount: 3);
        var result = complex.Compile();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Id, Is.EqualTo("5_1"));
        Assert.That(result[1].Id, Is.EqualTo("5_2"));
        Assert.That(result[2].Id, Is.EqualTo("5_3"));
        // All should trace back to source 5
        Assert.That(result.All(e => e.SourceId == 5), Is.True);
    }

    [Test]
    public void Compile_Should_Inherit_Condition()
    {
        var complex = new MockComplexEntry(3, "simple_text")
        {
            Condition = "flag == true"
        };
        var result = complex.Compile();

        Assert.That(result[0].Condition, Is.EqualTo("flag == true"));
    }
}
