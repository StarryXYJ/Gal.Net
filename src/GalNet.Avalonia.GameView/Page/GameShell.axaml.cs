using Avalonia.Controls;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Reusable page host. Editors and players inject different services but reuse this layout.</summary>
public partial class GameShell : UserControl
{
    private readonly GameShellViewModel _viewModel;
    private readonly IPageViewFactory _views;

    public GameShell(GameShellViewModel viewModel, IPageViewFactory views)
    {
        _viewModel = viewModel;
        _views = views;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GameShellViewModel.CurrentViewModel)) ShowCurrentPage();
        };
        ShowCurrentPage();
    }

    private void ShowCurrentPage()
    {
        PageHost.Content = _viewModel.CurrentViewModel is { } viewModel ? _views.Create(viewModel) : null;
    }
}
