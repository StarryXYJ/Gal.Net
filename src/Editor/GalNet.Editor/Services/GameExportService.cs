using System.Threading;
using System.Threading.Tasks;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class GameExportService(
    IProjectService projects,
    IResourceTypeCatalog resourceTypes,
    IGalleryTypeCatalog galleryTypes) : IGameExportService
{
    public Task<GameExportResult> ExportAsync(string outputDirectory, CancellationToken cancellationToken = default)
        => ExportContentAsync(outputDirectory, directory: false, cancellationToken);

    public Task<GameExportResult> ExportDirectoryAsync(string outputDirectory, CancellationToken cancellationToken = default)
        => ExportContentAsync(outputDirectory, directory: true, cancellationToken);

    private async Task<GameExportResult> ExportContentAsync(string outputDirectory, bool directory, CancellationToken cancellationToken)
    {
        var project = projects.Current;
        if (project is null) return GameExportResult.Failed("Open a project before exporting.");
        await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync(cancellationToken);
        using var capture = await project.Services.GetRequiredService<GalNet.Editor.History.EditorHistories>().Content
            .AcquireCaptureAsync(cancellationToken: cancellationToken);
        if (!ReferenceEquals(project, projects.Current)) return GameExportResult.Failed("The project was closed.");
        var catalog = project.Services.GetRequiredService<IEntryCatalog>();
        return await Task.Run(async () =>
        {
            if (directory)
            {
                var result = await GamePackageExporter.ExportDirectoryAsync(project.Id, project.Name, project.RootPath, outputDirectory,
                    resourceTypes, galleryTypes, catalog, project.Configurations, cancellationToken);
                return new GameExportResult(result.Success, result.ContentDirectory, result.Error);
            }
            else
            {
                var result = await GamePackageExporter.ExportAsync(project.Id, project.Name, project.RootPath, outputDirectory,
                    resourceTypes, galleryTypes, catalog, project.Configurations, cancellationToken);
                return new GameExportResult(result.Success, result.PackagePath, result.Error);
            }
        }, cancellationToken);
    }
}
