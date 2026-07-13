using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Abstraction.Extensibility;

namespace GalNet.Editor.Inspector.ViewModels;

/// <summary>Hosts the optional inspector supplied by the currently active dock panel.</summary>
public sealed partial class InspectorHostViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IEditorExtensionRegistry _extensions;
    private IInspectorControlViewModel? _currentInspectorViewModel;
    private IInspectorControlContribution? _currentContribution;

    [ObservableProperty] private Avalonia.Controls.Control? _currentInspectorView;

    public InspectorHostViewModel(IServiceProvider services, IEditorExtensionRegistry extensions)
    {
        _services = services;
        _extensions = extensions;
    }

    public void ShowInspectorFor(string panelId, object dockViewModel)
    {
        var inspector = _extensions.FindDockPanel(panelId)?.Inspector;
        ReplaceInspector(inspector?.CreateViewModel(_services, dockViewModel), inspector);
    }

    public void ClearInspector() => ReplaceInspector(null, null);

    private void ReplaceInspector(IInspectorControlViewModel? viewModel, IInspectorControlContribution? contribution)
    {
        if (ReferenceEquals(viewModel, _currentInspectorViewModel))
            return;

        if (_currentInspectorViewModel is not null)
        {
            _currentInspectorViewModel.PropertyChanged -= OnInspectorPropertyChanged;
            _currentInspectorViewModel.Dispose();
        }

        _currentInspectorViewModel = viewModel;
        _currentContribution = contribution;
        if (viewModel is null || contribution is null)
        {
            CurrentInspectorView = null;
            return;
        }

        viewModel.PropertyChanged += OnInspectorPropertyChanged;
        CurrentInspectorView = viewModel.IsAvailable
            ? contribution.CreateView(_services, viewModel) as Avalonia.Controls.Control
            : null;
    }

    private void OnInspectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IInspectorControlViewModel.IsAvailable) && sender is IInspectorControlViewModel vm)
            CurrentInspectorView = vm.IsAvailable && _currentContribution is not null
                ? _currentContribution.CreateView(_services, vm) as Avalonia.Controls.Control
                : null;
    }

    public void Dispose() => ClearInspector();
}
