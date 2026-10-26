using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.Configuration;

namespace GalNet.Project.Contracts;

/// <summary>Default authoring contract deployed when creating an Editor project.</summary>
public static class ProjectSchemas
{
    public static ProjectConfigurationSchema Configurations { get; } = DefaultProjectConfiguration.Schema;
    public static IReadOnlyList<IEntryModule> Modules { get; } = Array.AsReadOnly(BuiltinSchemas.CreateAllModules().ToArray());
}
