using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Configuration;

namespace GalNet.Project.Contracts;

/// <summary>Single source of authoring contracts shared by the sample game and tools.</summary>
public static class ProjectSchemas
{
    public static ProjectConfigurationSchema Configurations { get; } = DefaultProjectConfiguration.Schema;
    public static IEntryCatalog CreateCatalog() => new TargetProfileEntryCatalog(Modules);

    public static IReadOnlyList<IEntryModule> Modules { get; } = Array.AsReadOnly(
        BuiltinSchemas.CreateAllModules()
            .Append(ScreenshotSchema.CreateModule())
            .ToArray());
}
