using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;

namespace GalNet.Project.Contracts;

public static class ProjectSchemas
{
    public static IReadOnlyList<IEntryModule> Modules { get; } = Array.AsReadOnly<IEntryModule>(
    [
        BuiltinSchemas.CreateDialogueModule(),
        new DefaultEntryModule("collision", BuiltinSchemas.CreateDialogueModule().PrimitiveEntries.Values)
    ]);
}
