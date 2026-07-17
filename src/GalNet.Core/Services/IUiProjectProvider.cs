using GalNet.Core.UI;

namespace GalNet.Core.Services;

public interface IUiProjectProvider
{
    UiProject Current { get; }
    event Action? Changed;
    /// <summary>Replaces the current project UI projection with a detached document.</summary>
    void Replace(UiProject project);
    /// <summary>Signals an in-memory edit after callers mutate the project-owned model.</summary>
    void NotifyChanged();
    Task SaveAsync(CancellationToken cancellationToken = default);
}
