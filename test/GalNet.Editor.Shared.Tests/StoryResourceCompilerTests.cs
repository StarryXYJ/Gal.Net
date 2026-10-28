using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class StoryResourceCompilerTests
{
    [Test]
    public void PrimitiveResourceReferencesComeFromExplicitSchemaRatherThanStringShape()
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var group = new CompiledGroupDocument { Entries = [new() { Id = "show", TypeId = "layer.show",
            Arguments = JsonSerializer.SerializeToElement(new { handleId = "not-a-resource", assetId = "hero", z = 0, opacity = 1 }) }] };
        var result = StoryResourceCompiler.Dependencies(group, catalog);
        Assert.That(result.Complete, Is.True);
        Assert.That(result.Direct.Select(item => (item.AssetId, item.ExpectedTypeId)), Is.EqualTo(new[] { ("hero", "sprite") }));
    }

    [Test]
    public void UnknownInstructionAndCustomInvocationCannotPretendToHaveNoDependencies()
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        foreach (var type in new[] { "unknown.command", "custom.invoke" })
        {
            var result = StoryResourceCompiler.Dependencies(new() { Entries = [new() { Id = "entry", TypeId = type,
                Arguments = JsonSerializer.SerializeToElement(new { action = "project", payload = "not-a-resource" }) }] }, catalog);
            Assert.That(result.Complete, Is.False);
        }
    }
}
