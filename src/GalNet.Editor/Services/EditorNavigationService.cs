using Microsoft.Extensions.DependencyInjection;
using GalNet.Presentation.Abstractions.Navigation;

namespace GalNet.Editor.Services;

/// <summary>Editor-only navigation over startup, project and Dock workspace pages.</summary>
public sealed class EditorNavigationService : NavigationHistory<object>, INavigationService
{
    private readonly IServiceProvider _services;
    private readonly EditorNavigationService? _parent;
    private readonly Dictionary<Type, Type> _views = [];

    public object? CurrentPage => Current;
    public event Action<object?>? CurrentPageChanged;

    public EditorNavigationService(IServiceProvider services) => _services = services;
    private EditorNavigationService(IServiceProvider services, EditorNavigationService parent) { _services = services; _parent = parent; }

    public void RegisterMap(Type viewModelType, Type viewType) => _views[viewModelType] = viewType;
    public Type? GetRegisteredViewType(Type viewModelType) =>
        _views.TryGetValue(viewModelType, out var view) ? view : _parent?.GetRegisteredViewType(viewModelType);
    public void NavigateTo<TViewModel>() where TViewModel : class =>
        NavigateTo(_services.GetRequiredService<TViewModel>());
    public void NavigateTo(object viewModel) => Push(viewModel);
    public void ResetTo<TViewModel>() where TViewModel : class => ResetTo(_services.GetRequiredService<TViewModel>());
    public void ResetTo(object viewModel) => Replace(viewModel);
    public void GoBack() => TryGoBack();
    public void Clear() => ClearCurrent();
    public INavigationService CreateScope() => new EditorNavigationService(_services, this);

    protected override void OnCurrentChanged(object? page) => CurrentPageChanged?.Invoke(page);
}
