using GalNet.Core.UI;

namespace GalNet.Core.Services;

public interface IUiProjectProvider
{
    UiProject Current { get; }
    event Action? Changed;
    /// <summary>Signals an in-memory edit after callers mutate the project-owned model.</summary>
    void NotifyChanged();
    IReadOnlyList<UiValidationError> Validate();
    Task SaveAsync(CancellationToken cancellationToken = default);
}
