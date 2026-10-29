using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Core.Configuration;

namespace GalNet.Assets;

public static class ResourceOverrideBuilder
{
    public static byte[] Build(string projectId, ResourceOverrideDefinition definition, IReadOnlyList<IGameFile> available,
        IResourceTypeCatalog types, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(definition);
        var control = new PakDescriptor
        {
            Kind = "override",
            ProjectId = projectId,
            PackageId = definition.Id,
            Priority = definition.Priority,
            Mappings = definition.Mappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        control.Validate();
        if (control.Mappings.Count == 0) throw new InvalidDataException("A resource override must contain at least one mapping.");
        var files = available.ToDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var (source, target) in control.Mappings)
        {
            if (!files.TryGetValue(source, out var original) || !files.TryGetValue(target, out var replacement))
                throw new InvalidDataException($"Override '{source}' → '{target}' requires both author resources.");
            ValidateCompatibility(original, replacement);
        }
        var closure = AssetDependencyCollector.Collect(available, control.Mappings.Values.Concat(definition.IncludeResources), types,
            declaredComplete: definition.DeclaredCompleteResources, cancellationToken: ct);
        if (!closure.Complete) throw new InvalidDataException("Override dependencies are incomplete: " + string.Join(", ", closure.IncompleteAssetIds));
        if (closure.Files.Any(file => file.TypeId is "graph" or "group")) throw new InvalidDataException("Resource overrides cannot carry graph/group resources.");
        return PakBuilder.Build("assets", closure.Files, types, descriptor: control);
    }

    public static void ValidateCompatibility(IGameFile original, IGameFile replacement)
    {
        if (original.TypeId is "graph" or "group" || replacement.TypeId != original.TypeId)
            throw new InvalidDataException($"Resource override '{original.Id}' has an incompatible resource type.");
        if (original.TypeId != "image-variants") return;
        var before = ImageVariantsCodec.Decode(original.ReadAllBytes());
        var after = ImageVariantsCodec.Decode(replacement.ReadAllBytes());
        if (!before.Dimensions.Select(d => d.Key).SequenceEqual(after.Dimensions.Select(d => d.Key), StringComparer.Ordinal)
            || before.Variants.Any(variant => !after.Variants.Any(target => target.Key == variant.Key)))
            throw new InvalidDataException($"Replacement atlas '{replacement.Id}' must preserve dimension keys and original variant keys.");
    }
}
