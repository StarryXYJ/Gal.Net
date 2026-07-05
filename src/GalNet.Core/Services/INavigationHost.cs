namespace GalNet.Core.Services;

/// <summary>
/// Lightweight navigation mediator between ViewModels and the host View.
/// Implemented by GameHostView, injected into Screen ViewModels.
/// </summary>
public interface INavigationHost
{
    /// <summary>Replace the main content area.</summary>
    void NavigateTo(string key, object? parameter = null);

    /// <summary>Show a modal dialog via Ursa DialogHost.</summary>
    void ShowModal(string key, object? parameter = null);

    /// <summary>Close the current modal dialog.</summary>
    void CloseModal();

    /// <summary>Show a toast notification.</summary>
    void ShowToast(string message);
}
