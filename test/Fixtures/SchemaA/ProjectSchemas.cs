using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;
using System.Text.Json;

namespace GalNet.Project.Contracts;

public static class ProjectSchemas
{
    public static IReadOnlyList<IEntryModule> Modules { get; } = Array.AsReadOnly<IEntryModule>(
    [
        BuiltinSchemas.CreateDialogueModule(),
        new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase(SchemaDependency.Names.EntryType, DynamicParameterTable.Empty)],
            [new DefaultCompositeEntryBase("custom.bundle", DynamicParameterTable.Empty, () => new BundleEntry())])
    ]);
}

internal sealed class BundleEntry : CompositeEntry
{
    public override string Type => "custom.bundle";
    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) =>
        [new PrimitiveEntry(SchemaDependency.Names.EntryType, JsonSerializer.SerializeToElement(new { })) { Condition = context.Condition }];
}
