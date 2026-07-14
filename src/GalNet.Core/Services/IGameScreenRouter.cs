namespace GalNet.Core.Services;

/// <summary>Game-only navigation based on extensible screen category keys.</summary>
public interface IGameScreenRouter
{
    string? CurrentCategory { get; }
    event Action<object?>? CurrentScreenChanged;
    Task NavigateAsync(string categoryKey, object? parameter = null, CancellationToken cancellationToken = default);
    bool CanNavigate(string categoryKey);
}
