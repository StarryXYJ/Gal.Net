using GalNet.Core.Parameters;

namespace GalNet.Core.Configuration;

public sealed class ResourceOverrideOptions
{
    public List<ResourceOverrideDefinition> Packages { get; set; } = [];
    public List<string> AdditionalOriginals { get; set; } = [];
    public static ConfigurationSection<ResourceOverrideOptions> Section { get; } = new("galnet.resourceOverrides",
        [new("packages", new(typeof(List<ResourceOverrideDefinition>))), new("additionalOriginals", new(typeof(List<string>)))], value =>
        {
            if (value.Packages is null || value.AdditionalOriginals is null || value.Packages.Any(package => package is null
                || string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Name) || package.Mappings is null
                || package.IncludeResources is null || package.DeclaredCompleteResources is null)
                || value.Packages.Select(package => package.Id).Distinct(StringComparer.Ordinal).Count() != value.Packages.Count)
                throw new InvalidDataException("Resource override definitions require unique IDs, names and non-null resource lists.");
        });
}
