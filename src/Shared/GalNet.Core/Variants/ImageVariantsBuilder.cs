using System.Globalization;

namespace GalNet.Core.Variants;

/// <summary>Compiles already-enumerated sprite facts; never reads a directory or opens an image.</summary>
public static class ImageVariantsBuilder
{
    public static ImageVariantsBuildResult Build(ImageVariantsSource source, IEnumerable<ImageVariantFile> files)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(files);
        var diagnostics = new List<ImageVariantsDiagnostic>();
        void Error(string code, string path, string message) => diagnostics.Add(new(code, path, message));

        if (source.Version != ImageVariantsSource.CurrentVersion)
            Error("unsupported-version", "/version", $"Unsupported image variants version '{source.Version}'.");
        if (string.IsNullOrWhiteSpace(source.Folder))
            Error("missing-folder", "/folder", "An author folder is required.");

        var variants = new List<ImageVariant>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var file in files)
        {
            var path = $"/files/{index++}";
            if (file is null)
            {
                Error("missing-file", path, "A file fact cannot be null.");
                continue;
            }
            if (file.TypeId != "sprite")
                Error("invalid-resource-type", path + "/type", $"'{file.FileName}' must be a sprite resource.");
            if (string.IsNullOrWhiteSpace(file.AssetId))
                Error("missing-asset-id", path + "/assetId", $"'{file.FileName}' must have a resource ID.");
            var name = file.FileName;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\']) >= 0)
            {
                Error("invalid-file-name", path + "/fileName", "Expected a direct child file name, without directory segments.");
                continue;
            }
            var dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1)
            {
                Error("invalid-file-name", path + "/fileName", $"'{name}' must have a nonempty base name and extension.");
                continue;
            }
            var key = name[..dot];
            if (!names.Add(key))
                Error("duplicate-variant", path + "/fileName", $"Variant '{key}' duplicates a base name, including case-only differences.");
            var values = key.Split('_');
            if (values.Any(string.IsNullOrEmpty))
                Error("empty-segment", path + "/fileName", $"'{name}' contains an empty dimension segment.");
            variants.Add(new ImageVariant(key, file.AssetId, values));
        }
        if (variants.Count == 0) Error("empty-variants", "/files", "At least one variant is required.");
        if (string.IsNullOrEmpty(source.Default) || !variants.Any(item => item.Key == source.Default))
            Error("invalid-default", "/default", "Default must name an existing exact variant key.");

        var count = variants.Count == 0 ? 0 : variants.Max(item => item.Values.Count);
        var dimensions = new List<ImageVariantDimension>();
        // Author dimensions name positions; files alone determine the active structure.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (source.Dimensions is not null)
            for (var position = 0; position < source.Dimensions.Count; position++)
            {
                var item = source.Dimensions[position];
                var path = $"/dimensions/{position}/key";
                if (item is null || string.IsNullOrWhiteSpace(item.Key))
                    Error("invalid-dimension-key", path, "A nonempty dimension key is required.");
                else if (!keys.Add(item.Key))
                    Error("duplicate-dimension-key", path, $"Dimension '{item.Key}' is repeated.");
            }
        for (var position = 0; position < count; position++)
        {
            var number = (position + 1).ToString(CultureInfo.InvariantCulture);
            var item = source.Dimensions is { } configured && position < configured.Count ? configured[position] : null;
            var key = item?.Key;
            if (key is null)
            {
                key = "d" + number;
                while (!keys.Add(key)) key += "_";
            }
            dimensions.Add(new ImageVariantDimension(key, item?.Label ?? number, Options(position)));
        }

        return diagnostics.Count > 0
            ? new ImageVariantsBuildResult(null, diagnostics)
            : new ImageVariantsBuildResult(new ImageVariantsDefinition(dimensions, variants, source.Default), diagnostics);

        IEnumerable<string> Options(int position) => variants.Where(item => item.Values.Count > position).Select(item => item.Values[position]);
    }
}
