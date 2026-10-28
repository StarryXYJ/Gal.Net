using GalNet.Core.Parameters;

namespace GalNet.Core.Configuration;

/// <summary>Explicit roots and dynamic-reference declarations used by publication.</summary>
public sealed class ContentPackagingOptions
{
    public long MaxPakBytes { get; set; } = 64 * 1024 * 1024;
    public List<string> IncludeResources { get; set; } = [];
    public List<string> DeclaredCompleteResources { get; set; } = [];

    public static ConfigurationSection<ContentPackagingOptions> Section { get; } = new("galnet.packaging",
    [new("maxPakBytes", new(typeof(long))), new("includeResources", new(typeof(List<string>))),
        new("declaredCompleteResources", new(typeof(List<string>)))], value =>
    {
        if (value.MaxPakBytes < 1 || value.IncludeResources is null || value.DeclaredCompleteResources is null)
            throw new InvalidDataException("Packaging requires a positive PAK size and non-null resource lists.");
    });
}
