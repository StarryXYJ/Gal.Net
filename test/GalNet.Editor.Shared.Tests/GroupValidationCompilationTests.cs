using System.Text.Json;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class GroupValidationCompilationTests
{
    [Test]
    public void SemanticValidationDoesNotRepeatPrimitiveValidation()
    {
        var calls = 0;
        var catalog = new TargetProfileEntryCatalog([new DefaultEntryModule("test",
            [new DefaultPrimitiveEntryBase("test.run", new DynamicParameterTable([], _ => calls++))])]);
        var raw = JsonSerializer.SerializeToElement(new
        {
            version = 2,
            kind = "Raw",
            entries = new[] { new { id = "one", type = "test.run", parameters = new { } } }
        });
        var diagnostics = new List<ToolDiagnostic>();
        Assert.That(ProjectValidationService.ValidateGroupDocument(raw, "group.rawgalgroup", diagnostics, catalog), Is.Not.Null);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(calls, Is.EqualTo(1));
    }
}
