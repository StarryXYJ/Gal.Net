namespace GalNet.Editor.Abstraction.Services;

/// <summary>
/// Editor-workspace navigation for startup, project and Dock pages.
/// It deliberately does not model game-shell navigation.
/// </summary>
public interface INavigationService
{
    object? CurrentPage { get; }
    bool CanGoBack { get; }
    event Action<object?>? CurrentPageChanged;

    void RegisterMap(Type viewModelType, Type viewType);
    Type? GetRegisteredViewType(Type viewModelType);
    void NavigateTo<TViewModel>() where TViewModel : class;
    void NavigateTo(object viewModel);
    void ResetTo(object viewModel);
    void ResetTo<TViewModel>() where TViewModel : class;
    void GoBack();
    void Clear();
    INavigationService CreateScope();
}
