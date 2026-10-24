using System.Text;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Core.Variants;

namespace GalNet.Editor.Shared.Services;

public sealed record ImageVariantsAuthoringSnapshot(ImageVariantsSource Source, IReadOnlyList<ImageVariantFile> Files,
    IReadOnlyDictionary<string, string> ImagePaths, ImageVariantsBuildResult Build);

/// <summary>Read-only author inspection, using the same path, metadata and definition rules as compilation.</summary>
public static class ImageVariantsAuthoring
{
    public static ImageVariantsAuthoringSnapshot Scan(string root, string descriptorPath, string json, IResourceTypeCatalog types)
    {
        descriptorPath = ProjectFilePaths.Normalize(descriptorPath);
        if (!descriptorPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An atlas must be inside Assets.");
        var source = ImageVariantsCodec.ReadSource(Encoding.UTF8.GetBytes(json));
        var folder = LocalAssetCompiler.ResolveFolder(descriptorPath[7..], source.Folder);
        var reader = new ProjectRawReader(root);
        var full = reader.Resolve("Assets/" + folder);
        var files = new List<ImageVariantFile>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var codec = new AssetMetaCodec(types);
        foreach (var file in Directory.EnumerateFiles(full).Order(StringComparer.Ordinal))
        {
            var relative = reader.Relative(file);
            _ = reader.Resolve(relative); // Reject links even when they have a supported extension.
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            var spriteExtension = types.TryGetByExtension(Path.GetExtension(file), out var type) && type.TypeId == "sprite";
            var sidecar = reader.Resolve(relative + ".meta");
            AssetMeta? meta = File.Exists(sidecar) ? codec.Deserialize(File.ReadAllText(sidecar), relative[7..]) : null;
            if (!spriteExtension && meta?.TypeId != "sprite") continue;
            if (meta is null) throw new InvalidDataException($"Image '{relative[7..]}' has no metadata.");
            if (!paths.TryAdd(meta.Id, relative)) throw new InvalidDataException($"Duplicate resource ID '{meta.Id}'.");
            files.Add(new(Path.GetFileName(file), meta.Id, meta.TypeId));
        }
        return new(source, files.AsReadOnly(), paths, ImageVariantsBuilder.Build(source, files));
    }

    public static string Create(string root, string descriptorPath, IResourceTypeCatalog types)
    {
        var source = new ImageVariantsSource { Folder = ".", Default = "" };
        var snapshot = Scan(root, descriptorPath, System.Text.Json.JsonSerializer.Serialize(source), types);
        source.Default = snapshot.Files.Select(file => Path.GetFileNameWithoutExtension(file.FileName)).Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidDataException("The folder contains no imported images.");
        var build = ImageVariantsBuilder.Build(source, snapshot.Files);
        if (build.Definition is null) throw new InvalidDataException(string.Join("\n", build.Diagnostics.Select(item => item.Message)));
        return System.Text.Json.JsonSerializer.Serialize(source, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
