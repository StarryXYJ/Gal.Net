using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Shared.Services;

internal static class ImageVariantsValidation
{
    public static IReadOnlyList<ToolDiagnostic> Validate(string projectRoot, IResourceTypeCatalog resourceTypes)
    {
        var diagnostics = new List<ToolDiagnostic>();
        var reader = new ProjectRawReader(projectRoot);
        var assetsRoot = reader.Resolve("Assets");
        if (!Directory.Exists(assetsRoot)) return diagnostics;
        var files = reader.Files("Assets", diagnostics);
        if (!files.Any(path => resourceTypes.TryGetByExtension(Path.GetExtension(path), out var type) && type.TypeId == ImageVariantsCodec.ResourceType
            || IsDeclaredVariants(reader, path)))
            return diagnostics;
        try
        {
            using var provider = new LocalFileProvider(assetsRoot, resourceTypes);
            using var archive = provider.OpenArchive("assets");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            diagnostics.Add(new("variants.invalid", "error", exception.Message, "Assets"));
        }
        return diagnostics;
    }

    private static bool IsDeclaredVariants(ProjectRawReader reader, string path)
    {
        if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reader.Resolve(path)));
            return json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && json.RootElement.EnumerateObject().Any(property => property.Name.Equals("type", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    && string.Equals(property.Value.GetString()?.Trim(), ImageVariantsCodec.ResourceType, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false; // General project metadata validation reports unreadable/invalid sidecars.
        }
    }
}
