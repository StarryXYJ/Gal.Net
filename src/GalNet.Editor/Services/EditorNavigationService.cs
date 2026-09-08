using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly NavigationService? _parent;
    private readonly Dictionary<Type, Type> _views = [];
    private readonly Stack<object> _history = [];
    private object? _current;

    public object? CurrentPage => _current;
    public bool CanGoBack => _history.Count > 0;
    public event Action<object?>? CurrentPageChanged;

    public NavigationService(IServiceProvider services) => _services = services;
    private NavigationService(IServiceProvider services, NavigationService parent) { _services = services; _parent = parent; }

    public void RegisterMap(Type viewModelType, Type viewType) => _views[viewModelType] = viewType;
    public Type? GetRegisteredViewType(Type viewModelType) =>
        _views.TryGetValue(viewModelType, out var view) ? view : _parent?.GetRegisteredViewType(viewModelType);
    public void NavigateTo<TViewModel>() where TViewModel : class =>
        NavigateTo(_services.GetRequiredService<TViewModel>());
    public void NavigateTo(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (_current is not null) _history.Push(_current);
        SetCurrent(viewModel);
    }
    public void ResetTo<TViewModel>() where TViewModel : class => ResetTo(_services.GetRequiredService<TViewModel>());
    public void ResetTo(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _history.Clear();
        SetCurrent(viewModel);
    }
    public void GoBack() { if (_history.TryPop(out var page)) SetCurrent(page); }
    public void Clear() { _history.Clear(); SetCurrent(null); }
    public INavigationService CreateScope() => new NavigationService(_services, this);
    private void SetCurrent(object? page) { _current = page; CurrentPageChanged?.Invoke(page); }
}
