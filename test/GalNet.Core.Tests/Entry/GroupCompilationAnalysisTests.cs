using System.Text.Json;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;

namespace GeneralTest.Entry;

public sealed class GroupCompilationAnalysisTests
{
    [Test]
    public void AnalysisValidatesEachPrimitiveOnceAndRetainsWholeGroupNumbering()
    {
        var validations = 0;
        var schema = new DynamicParameterTable([new("value", typeof(int), isRequired: true)], _ => validations++);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.run", schema)])]);
        var source = new GroupDocument { Entries = [Entry("a", 1), Entry("b", 2)] };
        var analysis = GalgroupCompiler.Analyze(source, catalog);
        Assert.That(analysis.Issues, Is.Empty);
        Assert.That(validations, Is.EqualTo(2));
        Assert.That(analysis.Compilation!.Document.Entries.Select(item => item.Id), Is.EqualTo(new[] { "a#1", "b#1" }));
        Assert.That(analysis.Compilation.SourceMap["b"], Is.EqualTo(new[] { "b#1" }));
    }

    [Test]
    public void AnalysisCollectsAllEntryErrorsAndDoesNotExposePartialCompilation()
    {
        var schema = new DynamicParameterTable([new("value", typeof(int), isRequired: true)]);
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test", [new DefaultPrimitiveEntryBase("test.run", schema)])]);
        var source = new GroupDocument { Entries = [Entry("a", 1), Entry("b", "bad"), Entry("c", "also bad")] };
        var analysis = GalgroupCompiler.Analyze(source, catalog);
        Assert.That(analysis.Compilation, Is.Null);
        Assert.That(analysis.Issues.Select(item => item.SourceIndex), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(analysis.Issues.All(item => item.IsParameterError), Is.True);
        Assert.That(() => GalgroupCompiler.Compile(source, catalog), Throws.TypeOf<InvalidDataException>());
    }

    private static GroupEntryDocument Entry(string id, object value) => new()
    {
        Id = id,
        Type = "test.run",
        Parameters = new() { ["value"] = JsonSerializer.SerializeToElement(value) }
    };
}
