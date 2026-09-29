using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Presentation.Abstractions.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Avalonia.GameView.Navigation;

/// <summary>Framework-neutral identity for a navigable page within an Avalonia game scope.</summary>
public abstract class PageViewModelBase : ObservableObject;

/// <summary>Optional activation contract for per-navigation data that must not enter DI.</summary>
public interface IActivatablePageViewModel<in TArgs>
{
    Task ActivateAsync(TArgs args, CancellationToken cancellationToken = default);
}

/// <summary>Visual policy for a logical page change.</summary>
public enum NavigationTransition { None, CrossFade }

public sealed class GameNavigationChangedEventArgs(
    PageViewModelBase? previous,
    PageViewModelBase? current,
    NavigationTransition transition) : EventArgs
{
    public PageViewModelBase? Previous { get; } = previous;
    public PageViewModelBase? Current { get; } = current;
    public NavigationTransition Transition { get; } = transition;
}

/// <summary>UI bridge used to present a page after navigation state changes.</summary>
public interface IGameNavigationTransitionCoordinator
{
    /// <summary>Completes after the requested page has been presented by the visual host.</summary>
    Task PresentPageAsync(
        PageViewModelBase? viewModel,
        NavigationTransition transition,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

}

/// <summary>Attached by GameShell after construction; remains a harmless no-op for headless hosts and tests.</summary>
public sealed class GameNavigationTransitionCoordinator : IGameNavigationTransitionCoordinator
{
    private Func<PageViewModelBase?, NavigationTransition, Task>? _presentPage;

    public void Attach(Func<PageViewModelBase?, NavigationTransition, Task> presentPage)
    {
        _presentPage = presentPage;
    }

    public void Detach()
    {
        _presentPage = null;
    }

    public Task PresentPageAsync(
        PageViewModelBase? viewModel,
        NavigationTransition transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _presentPage?.Invoke(viewModel, transition) ?? Task.CompletedTask;
    }

}

public interface IGameNavigationService
{
    PageViewModelBase? CurrentViewModel { get; }
    bool CanGoBack { get; }
    event EventHandler? CurrentViewModelChanged;
    event EventHandler<GameNavigationChangedEventArgs>? Navigated;

    void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase;
    Task NavigateAsync(
        PageViewModelBase viewModel,
        NavigationTransition transition = NavigationTransition.CrossFade,
        CancellationToken cancellationToken = default);
    Task NavigateAsync<TViewModel, TArgs>(TArgs args, CancellationToken cancellationToken)
        where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs> =>
        NavigateAsync<TViewModel, TArgs>(args, NavigationTransition.CrossFade, cancellationToken);
    Task NavigateAsync<TViewModel, TArgs>(TArgs args, NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
        where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs>;
    void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase;
    Task ResetToAsync<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
        where TViewModel : PageViewModelBase;
    void GoBack(NavigationTransition transition = NavigationTransition.CrossFade);
}

/// <summary>Resolves page VMs from the current game scope and owns only navigation state/history.</summary>
public sealed class GameNavigationService(IServiceProvider services, IGameNavigationTransitionCoordinator transitions)
    : NavigationHistory<PageViewModelBase>, IGameNavigationService
{
    private PageViewModelBase? _previous;
    private NavigationTransition _pendingTransition;

    public PageViewModelBase? CurrentViewModel => Current;
    public event EventHandler? CurrentViewModelChanged;
    public event EventHandler<GameNavigationChangedEventArgs>? Navigated;

    public void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase =>
        Navigate(services.GetRequiredService<TViewModel>(), transition);

    public Task NavigateAsync(
        PageViewModelBase viewModel,
        NavigationTransition transition = NavigationTransition.CrossFade,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return NavigateAndPresentAsync(viewModel, transition, cancellationToken);
    }

    public async Task NavigateAsync<TViewModel, TArgs>(TArgs args, NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
        where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs>
    {
        var viewModel = services.GetRequiredService<TViewModel>();
        await viewModel.ActivateAsync(args, cancellationToken);
        await NavigateAndPresentAsync(viewModel, transition, cancellationToken);
    }

    public Task NavigateAsync<TViewModel, TArgs>(TArgs args, CancellationToken cancellationToken)
        where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs> =>
        NavigateAsync<TViewModel, TArgs>(args, NavigationTransition.CrossFade, cancellationToken);

    public void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade) where TViewModel : PageViewModelBase =>
        Replace(services.GetRequiredService<TViewModel>(), transition);

    public async Task ResetToAsync<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
        where TViewModel : PageViewModelBase
    {
        var viewModel = services.GetRequiredService<TViewModel>();
        Log.Logger.Debug("Navigation reset: from={From}, to={To}, transition={Transition}",
            Current?.GetType().Name ?? "null", viewModel.GetType().Name, transition);
        await ReplaceAndPresentAsync(viewModel, transition, cancellationToken);
    }

    public void GoBack(NavigationTransition transition = NavigationTransition.CrossFade)
    {
        _previous = Current;
        _pendingTransition = transition;
        if (!TryGoBack())
        {
            _previous = null;
            _pendingTransition = NavigationTransition.None;
            return;
        }

        ObservePresentation(PresentPageAsync(Current, transition));
    }

    private void Navigate(PageViewModelBase viewModel, NavigationTransition transition)
    {
        _previous = Current;
        _pendingTransition = transition;
        Push(viewModel);
        ObservePresentation(PresentPageAsync(viewModel, transition));
    }

    private void Replace(PageViewModelBase viewModel, NavigationTransition transition)
    {
        _previous = Current;
        _pendingTransition = transition;
        base.Replace(viewModel);
        ObservePresentation(PresentPageAsync(viewModel, transition));
    }

    private async Task NavigateAndPresentAsync(
        PageViewModelBase viewModel,
        NavigationTransition transition,
        CancellationToken cancellationToken)
    {
        _previous = Current;
        _pendingTransition = transition;
        Push(viewModel);
        await PresentPageAsync(viewModel, transition, cancellationToken);
    }

    private async Task ReplaceAndPresentAsync<TViewModel>(
        TViewModel viewModel,
        NavigationTransition transition,
        CancellationToken cancellationToken)
        where TViewModel : PageViewModelBase
    {
        Log.Logger.Debug("Navigation presenting: from={From}, to={To}, transition={Transition}",
            Current?.GetType().Name ?? "null", viewModel.GetType().Name, transition);
        _previous = Current;
        _pendingTransition = transition;
        base.Replace(viewModel);
        await PresentPageAsync(viewModel, transition, cancellationToken);
    }

    private static async void ObservePresentation(Task presentation)
    {
        try { await presentation; }
        catch (OperationCanceledException) { }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
    }

    private Task PresentPageAsync(
        PageViewModelBase? viewModel,
        NavigationTransition transition,
        CancellationToken cancellationToken = default) =>
        transitions.PresentPageAsync(viewModel, transition, cancellationToken);

    protected override void OnCurrentChanged(PageViewModelBase? current)
    {
        var previous = _previous;
        var transition = _pendingTransition;
        _previous = null;
        _pendingTransition = NavigationTransition.None;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
        Navigated?.Invoke(this, new GameNavigationChangedEventArgs(previous, current, transition));
    }
}

public interface IPageViewFactory
{
    Control Create(PageViewModelBase viewModel);
}

/// <summary>Read-only VM-to-view mapping used after the game scope has been composed.</summary>
public interface IPageViewRegistry
{
    Type GetViewType(Type viewModelType);
}

/// <summary>Composition-time registry builder. Later registrations intentionally replace defaults.</summary>
public interface IPageViewRegistryBuilder
{
    void Register<TViewModel, TView>()
        where TViewModel : PageViewModelBase
        where TView : Control;

    IPageViewRegistry Build();
}

public sealed class PageViewRegistryBuilder : IPageViewRegistryBuilder
{
    private readonly Dictionary<Type, Type> _views = [];
    private bool _built;

    public void Register<TViewModel, TView>()
        where TViewModel : PageViewModelBase
        where TView : Control
    {
        if (_built) throw new InvalidOperationException("Page view mappings are immutable after composition.");
        _views[typeof(TViewModel)] = typeof(TView);
    }

    public IPageViewRegistry Build()
    {
        _built = true;
        return new PageViewRegistry(_views);
    }
}

/// <summary>Immutable runtime page mapping.</summary>
public sealed class PageViewRegistry : IPageViewRegistry
{
    private readonly IReadOnlyDictionary<Type, Type> _views;

    internal PageViewRegistry(IReadOnlyDictionary<Type, Type> views) =>
        _views = new Dictionary<Type, Type>(views);

    public Type GetViewType(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        if (_views.TryGetValue(viewModelType, out var viewType)) return viewType;
        throw new InvalidOperationException($"No page view is registered for '{viewModelType.FullName}'.");
    }
}

/// <summary>Resolves page controls from the same game scope and assigns their resolved VM.</summary>
public sealed class PageViewFactory(IServiceProvider services, IPageViewRegistry registry) : IPageViewFactory
{
    public Control Create(PageViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var viewType = registry.GetViewType(viewModel.GetType());
        var view = (Control)services.GetRequiredService(viewType);
        view.DataContext = viewModel;
        return view;
    }
}
