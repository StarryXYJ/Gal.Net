using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GeneralTest.Entry;

public sealed class BuiltinSchemaComponentsTests
{
    [TestCase("flow.wait", "duration", 0)]
    [TestCase("audio.play", "fadeIn", 0)]
    [TestCase("audio.stop", "fadeOut", 0)]
    [TestCase("particle.burst", "count", 1)]
    public void BasicRangesAreDeclaredAndEnforcedBySharedValueRules(string type, string name, int minimum)
    {
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var parameter = catalog.Get(type).DynamicParameters[name];
        Assert.That(parameter.TryGetConstraint("minimum", out var bound), Is.True);
        Assert.That(bound.GetInt32(), Is.EqualTo(minimum));
        Assert.That(ValueValidation.Validate(parameter.Value, JsonSerializer.SerializeToElement(minimum - 1))!.Code, Is.EqualTo("value.range"));
        Assert.Throws<InvalidDataException>(() => PrimitiveArgumentHelper.Normalize(catalog.Get(type).DynamicParameters,
            JsonSerializer.SerializeToElement(new Dictionary<string, object> { [name] = minimum - 1 })));
        var schema = ValueCodec.Describe(parameter.Value);
        if (parameter.Value.AllowNull) schema = schema.GetProperty("anyOf")[0];
        Assert.That(schema.GetProperty("minimum").GetInt32(), Is.EqualTo(minimum));
    }

    private static readonly string[] SelectedTypes = ["dialogue.text", "dialogue.show", "dialogue.hide", "flow.wait"];
    private static readonly string[] ContractDependencies = ["GalNet.Core"];

    [Test]
    public void AllReturnsIndependentModulesWithUniqueInstructionIds()
    {
        var modules = BuiltinSchemas.CreateAllModules();
        var catalog = new TargetProfileEntryCatalog(modules);
        Assert.Multiple(() =>
        {
            Assert.That(modules, Has.Count.EqualTo(14));
            Assert.That(modules.Select(module => module.Id).Distinct().Count(), Is.EqualTo(14));
            Assert.That(catalog.Definitions, Has.Count.EqualTo(52));
            Assert.That(catalog.Definitions.Count(entry => entry.Kind == EntryKind.Primitive), Is.EqualTo(38));
            Assert.That(catalog.Definitions.Count(entry => entry.Kind == EntryKind.Composite), Is.EqualTo(14));
            Assert.That(() => ((IList<IEntryModule>)modules).Clear(), Throws.TypeOf<NotSupportedException>());
        });
    }

    [Test]
    public void SelectedComponentsDoNotAddOtherDomains()
    {
        var catalog = new TargetProfileEntryCatalog(
        [
            BuiltinSchemas.CreateDialogueModule(),
            BuiltinSchemas.CreateFlowModule()
        ]);
        Assert.That(catalog.Definitions.Select(entry => entry.Type), Is.EquivalentTo(SelectedTypes));
        Assert.That(catalog.TryGet("audio.play", out _), Is.False);
        Assert.That(catalog.TryGet("transition.crossFade", out _), Is.False);
    }

    [Test]
    public void ContractsAssemblyDependsOnlyOnCoreAndFrameworkAssemblies()
    {
        var dependencies = typeof(BuiltinSchemas).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name).Where(name => name!.StartsWith("GalNet.", StringComparison.Ordinal));
        Assert.That(dependencies, Is.EquivalentTo(ContractDependencies));
    }
}
