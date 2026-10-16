using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Primitives;

namespace GalNet.Project.Contracts;

public static class ProjectSchemas
{
    public static IReadOnlyList<IEntryModule> Modules { get; } = Array.AsReadOnly<IEntryModule>([
        BuiltinSchemas.CreateFlowModule(),
        new DefaultEntryModule("flow", [new DefaultPrimitiveEntryBase("custom.marker", DynamicParameterTable.Empty)])
    ]);
}
