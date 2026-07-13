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
    private IInspectorControlViewModel? _currentInspectorControlViewModel;
    private IInspectorControlContribution? _currentContribution;

    [ObservableProperty] private IInspectorControlViewModel? _currentInspectorViewModel;

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
        if (ReferenceEquals(viewModel, _currentInspectorControlViewModel))
            return;

        if (_currentInspectorControlViewModel is not null)
        {
            _currentInspectorControlViewModel.PropertyChanged -= OnInspectorPropertyChanged;
            _currentInspectorControlViewModel.Dispose();
        }

        _currentInspectorControlViewModel = viewModel;
        _currentContribution = contribution;
        if (viewModel is null || contribution is null)
        {
            CurrentInspectorViewModel = null;
            return;
        }

        viewModel.PropertyChanged += OnInspectorPropertyChanged;
        CurrentInspectorViewModel = viewModel.IsAvailable ? viewModel : null;
    }

    private void OnInspectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IInspectorControlViewModel.IsAvailable) && sender is IInspectorControlViewModel vm)
            CurrentInspectorViewModel = vm.IsAvailable && _currentContribution is not null ? vm : null;
    }

    /// <summary>Creates a view for this host instance; views must never be shared across Dock presenters.</summary>
    public Avalonia.Controls.Control? CreateCurrentInspectorView() =>
        CurrentInspectorViewModel is not null && _currentContribution is not null
            ? _currentContribution.CreateView(_services, CurrentInspectorViewModel) as Avalonia.Controls.Control
            : null;

    public void Dispose() => ClearInspector();
}
