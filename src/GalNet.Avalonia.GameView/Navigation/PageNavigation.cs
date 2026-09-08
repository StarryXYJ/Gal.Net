using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Avalonia.GameView.Navigation;

/// <summary>Base type for view-first navigation. A page VM declares only its default Avalonia view.</summary>
public abstract class PageViewModelBase : ObservableObject
{
    public abstract Type ViewType { get; }
}

public abstract class PageViewModelBase<TView> : PageViewModelBase where TView : Control
{
    public override Type ViewType => typeof(TView);
}

/// <summary>Optional activation contract for per-navigation data that must not enter DI.</summary>
public interface IActivatablePageViewModel<in TArgs>
{
    Task ActivateAsync(TArgs args, CancellationToken cancellationToken = default);
}

public interface IGameNavigationService
{
    PageViewModelBase? CurrentViewModel { get; }
    bool CanGoBack { get; }
    event EventHandler? CurrentViewModelChanged;

    void Navigate<TViewModel>() where TViewModel : PageViewModelBase;
    Task NavigateAsync<TViewModel, TArgs>(TArgs args, CancellationToken cancellationToken = default)
        where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs>;
    void ResetTo<TViewModel>() where TViewModel : PageViewModelBase;
    void GoBack();
}

/// <summary>Resolves page VMs from the current game scope and owns only navigation state/history.</summary>
public sealed class GameNavigationService(IServiceProvider services) : IGameNavigationService
{
    private readonly Stack<PageViewModelBase> _history = [];

    public PageViewModelBase? CurrentViewModel { get; private set; }
    public bool CanGoBack => _history.Count > 0;
    public event EventHandler? CurrentViewModelChanged;

    public void Navigate<TViewModel>() where TViewModel : PageViewModelBase => Navigate(services.GetRequiredService<TViewModel>());

    public async Task NavigateAsync<TViewModel, TArgs>(TArgs args, CancellationToken cancellationToken = default)
        where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs>
    {
        var viewModel = services.GetRequiredService<TViewModel>();
        await viewModel.ActivateAsync(args, cancellationToken);
        Navigate(viewModel);
    }

    public void ResetTo<TViewModel>() where TViewModel : PageViewModelBase
    {
        _history.Clear();
        SetCurrent(services.GetRequiredService<TViewModel>());
    }

    public void GoBack()
    {
        if (_history.TryPop(out var previous)) SetCurrent(previous);
    }

    private void Navigate(PageViewModelBase viewModel)
    {
        if (CurrentViewModel is not null) _history.Push(CurrentViewModel);
        SetCurrent(viewModel);
    }

    private void SetCurrent(PageViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}

public interface IPageViewFactory
{
    Control Create(PageViewModelBase viewModel);
}

/// <summary>Resolves page controls from the same game scope and assigns their resolved VM.</summary>
public sealed class PageViewFactory(IServiceProvider services) : IPageViewFactory
{
    public Control Create(PageViewModelBase viewModel)
    {
        var view = (Control)services.GetRequiredService(viewModel.ViewType);
        view.DataContext = viewModel;
        return view;
    }
}
