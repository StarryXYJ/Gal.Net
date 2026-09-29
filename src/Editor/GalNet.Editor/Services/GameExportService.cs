using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Core.Assets;
using GalNet.Core.Gallery;
using System.Threading;
using System.Threading.Tasks;

namespace GalNet.Editor.Services;

public sealed class GameExportService(
    IProjectService projects,
    IResourceTypeCatalog resourceTypes,
    IGalleryTypeCatalog galleryTypes) : IGameExportService
{
    public async Task<GameExportResult> ExportAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        var project = projects.Current;
        if (project is null) return GameExportResult.Failed("Open a project before exporting.");
        var result = await GamePackageExporter.ExportAsync(project.Id, project.Name, project.RootPath, outputDirectory, resourceTypes, galleryTypes, cancellationToken);
        return new GameExportResult(result.Success, result.PackagePath, result.Error);
    }
}
