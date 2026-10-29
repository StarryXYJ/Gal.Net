namespace GalNet.Core.Assets;

/// <summary>Archive control data, separate from addressable resource metadata.</summary>
public sealed record PakDescriptor
{
    public string Kind { get; init; } = "base";
    public string ProjectId { get; init; } = "";
    public string BuildId { get; init; } = "";
    public string PackageId { get; init; } = "";
    public int PartIndex { get; init; }
    public int PartCount { get; init; } = 1;
    public int Priority { get; init; }
    public Dictionary<string, string> Mappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        if (Kind is not ("base" or "story" or "override"))
            throw new InvalidDataException($"Unsupported PAK kind '{Kind}'.");
        if (PartCount < 1 || PartIndex < 0 || PartIndex >= PartCount)
            throw new InvalidDataException("Invalid PAK part index/count.");
        if (ProjectId is null || BuildId is null || PackageId is null || Mappings is null)
            throw new InvalidDataException("PAK control fields cannot be null.");
        if (Kind != "override" && string.IsNullOrWhiteSpace(ProjectId) != string.IsNullOrWhiteSpace(BuildId))
            throw new InvalidDataException("PAK project and build identities must be supplied together.");
        if (Kind == "story" && (string.IsNullOrWhiteSpace(ProjectId) || string.IsNullOrWhiteSpace(PackageId)))
            throw new InvalidDataException("Story PAKs require project, build and package identities.");
        if (Kind != "override" && (Mappings.Count != 0 || Priority != 0))
            throw new InvalidDataException("Only override PAKs may contain mappings or priority.");
        if (Kind == "override" && (string.IsNullOrWhiteSpace(PackageId) || PartCount != 1))
            throw new InvalidDataException("Override PAKs require a package identity and one part.");
        if (Kind == "base" && PackageId.Length != 0)
            throw new InvalidDataException("Base PAKs cannot declare a story/override package identity.");
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, target) in Mappings)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)
                || source != source.Trim() || target != target.Trim() || !sources.Add(source)
                || StringComparer.OrdinalIgnoreCase.Equals(source, target))
                throw new InvalidDataException("Override mappings require distinct, non-empty canonical source/target IDs.");
        }
        var lookup = new Dictionary<string, string>(Mappings, StringComparer.OrdinalIgnoreCase);
        foreach (var source in lookup.Keys)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = source;
            while (lookup.TryGetValue(current, out var target))
            {
                if (!seen.Add(current)) throw new InvalidDataException("Override mappings cannot form a cycle.");
                current = target;
            }
        }
    }
}

public interface IPakArchive : IArchive
{
    PakDescriptor Descriptor { get; }
}
