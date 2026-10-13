namespace GalNet.Editor.Abstraction.Commands;

public interface IProjectRecycleBin
{
    bool IsSupported { get; }
    Task RecycleAsync(string projectDirectory, CancellationToken cancellationToken = default);
}

public sealed record ProjectDeleteReport(string ProjectPath, string Name, bool DryRun, bool Recycled);
