using Avalonia.Controls;

namespace GalNet.Avalonia.GameHost;

/// <summary>Application-owned UI and navigation, borrowed by the scope-owning player host.</summary>
public interface IPlayerApplication
{
    Control View { get; }
    void Attach();
    void Detach();
    void ShowTitle();
    Task StartNewGameAsync();
    Task<bool> HandleBackAsync();
    Task<bool> ConfirmExitAsync();
    Task<string?> SelectLanguageAsync(string preferred, CancellationToken cancellationToken);
    void CompleteStartup(string? errorMessage);
}
