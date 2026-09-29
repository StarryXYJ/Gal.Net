using Avalonia.Controls;
using GalNet.Editor.Inspector.ViewModels;

namespace GalNet.Editor.Inspector.Views;
public partial class InspectorHostView : UserControl
{
    public InspectorHostView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private InspectorHostViewModel? _viewModel;
    private void AttachViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as InspectorHostViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RefreshInspectorContent();
    }
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectorHostViewModel.CurrentInspectorViewModel)) RefreshInspectorContent();
    }
    private void RefreshInspectorContent()
    {
        InspectorContent.Content = null;
        InspectorContent.Content = _viewModel?.CreateCurrentInspectorView();
    }
}
