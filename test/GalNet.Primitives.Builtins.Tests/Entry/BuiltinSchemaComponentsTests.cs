using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;

namespace GeneralTest.Entry;

public sealed class BuiltinSchemaComponentsTests
{
    private static readonly string[] SelectedTypes = ["dialogue.text", "dialogue.show", "dialogue.hide", "flow.wait"];
    private static readonly string[] ContractDependencies = ["GalNet.Core"];

    [Test]
    public void AllReturnsIndependentModulesWithUniqueInstructionIds()
    {
        var modules = BuiltinSchemas.CreateAllModules();
        var catalog = new TargetProfileEntryCatalog(modules);
        Assert.Multiple(() =>
        {
            Assert.That(modules, Has.Count.EqualTo(13));
            Assert.That(modules.Select(module => module.Id).Distinct().Count(), Is.EqualTo(13));
            Assert.That(catalog.Definitions, Has.Count.EqualTo(42));
            Assert.That(catalog.Definitions.Count(entry => entry.Kind == EntryKind.Primitive), Is.EqualTo(34));
            Assert.That(catalog.Definitions.Count(entry => entry.Kind == EntryKind.Composite), Is.EqualTo(8));
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
