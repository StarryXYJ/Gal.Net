using GalNet.Core.Configuration;

namespace GalNet.GameHost.Content;

/// <summary>Composition-owned configuration declarations and localization projection.</summary>
public sealed record GameContentLoadOptions
{
    public ProjectConfigurationSchema Configurations { get; init; } = ProjectConfigurationSchema.Empty;
    public Func<ProjectConfiguration, string?>? SelectDefaultCulture { get; init; }
}
