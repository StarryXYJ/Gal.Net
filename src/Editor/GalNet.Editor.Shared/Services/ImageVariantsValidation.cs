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
        try
        {
            using var provider = new ProjectAssetProvider(projectRoot, resourceTypes);
            using var archive = provider.OpenArchive("assets");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            diagnostics.Add(new("variants.invalid", "error", exception.Message, "Assets/Buildin"));
        }
        return diagnostics;
    }

}
