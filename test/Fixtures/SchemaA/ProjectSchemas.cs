using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Configuration;
using GalNet.Core.Primitives;
using GalNet.Core.Parameters;

namespace GalNet.Project.Contracts;

public static class ProjectSchemas
{
    public static ProjectConfigurationSchema Configurations { get; } = new([
        DefaultProjectConfiguration.Section,
        new ConfigurationSection<ProjectOptions>("developer.options", [
            new("count", new ValueDescriptor(typeof(int), allowNull: false)),
            new("limit", new ValueDescriptor(typeof(int), allowNull: false)),
            new("caption", new ValueDescriptor(typeof(string), allowNull: true))
        ], options => { if (options.Count > options.Limit) throw new InvalidDataException("Count exceeds limit."); })
    ]);
    public static IReadOnlyList<IEntryModule> Modules { get; } = Array.AsReadOnly<IEntryModule>(
    [
        BuiltinSchemas.CreateDialogueModule(),
        new DefaultEntryModule("custom", [new DefaultPrimitiveEntryBase(SchemaDependency.Names.EntryType, DynamicParameterTable.Empty)],
            [new DefaultCompositeEntryBase("custom.bundle", DynamicParameterTable.Empty, () => new BundleEntry())])
    ]);
}

public sealed class ProjectOptions
{
    public int Count { get; set; } = 1;
    public int Limit { get; set; } = 10;
    public string? Caption { get; set; } = "Default";
}

internal sealed class BundleEntry : CompositeEntry
{
    public override string Type => "custom.bundle";
    public override IReadOnlyList<PrimitiveEntry> Compile(EntryCompileContext context) =>
        [new PrimitiveEntry(SchemaDependency.Names.EntryType, JsonSerializer.SerializeToElement(new { })) { Condition = context.Condition }];
}
