using System.Threading;
using System.Threading.Tasks;

namespace GalNet.Editor.Services.Interfaces;

public interface IGameExportService
{
    Task<GameExportResult> ExportAsync(string outputDirectory, CancellationToken cancellationToken = default);
}

public sealed record GameExportResult(bool Success, string? PackagePath, string? Error)
{
    public static GameExportResult Succeeded(string path) => new(true, path, null);
    public static GameExportResult Failed(string error) => new(false, null, error);
}
