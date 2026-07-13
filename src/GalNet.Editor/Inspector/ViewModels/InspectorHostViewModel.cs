using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Extensibility;

namespace GalNet.Editor.Inspector.ViewModels;

/// <summary>Hosts the optional inspector supplied by the currently active dock panel.</summary>
public sealed partial class InspectorHostViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IEditorExtensionRegistry _extensions;
    private IInspectorControlViewModel? _currentInspectorControlViewModel;
    private IInspectorControlContribution? _currentContribution;
    private object? _targetDockViewModel;

    [ObservableProperty] private IInspectorControlViewModel? _currentInspectorViewModel;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private string? _targetPanelId;

    public bool HasTarget => TargetPanelId is not null;

    public InspectorHostViewModel(IServiceProvider services, IEditorExtensionRegistry extensions)
    {
        _services = services;
        _extensions = extensions;
    }

    /// <summary>Follows a new dock target unless this inspector is locked.</summary>
    public void FollowInspectorFor(string panelId, object dockViewModel)
    {
        if (IsLocked)
            return;
        SetInspectorTarget(panelId, dockViewModel);
    }

    /// <summary>Clears an unlocked inspector when the active dock has no inspector contribution.</summary>
    public void FollowNoInspector()
    {
        if (!IsLocked)
            ClearInspector();
    }

    /// <summary>Sets the initial target for a newly-created inspector.</summary>
    public void SetInitialTarget(string panelId, object dockViewModel) => SetInspectorTarget(panelId, dockViewModel);

    private void SetInspectorTarget(string panelId, object dockViewModel)
    {
        if (TargetPanelId == panelId && ReferenceEquals(_targetDockViewModel, dockViewModel))
            return;

        var inspector = _extensions.FindDockPanel(panelId)?.Inspector;
        _targetDockViewModel = dockViewModel;
        TargetPanelId = panelId;
        ReplaceInspector(inspector?.CreateViewModel(_services, dockViewModel), inspector);
    }

    public void ClearInspector()
    {
        _targetDockViewModel = null;
        TargetPanelId = null;
        ReplaceInspector(null, null);
    }

    [RelayCommand]
    private void ToggleLock() => IsLocked = !IsLocked;

    partial void OnTargetPanelIdChanged(string? value) => OnPropertyChanged(nameof(HasTarget));

    partial void OnIsLockedChanged(bool value)
    {
        if (_currentInspectorControlViewModel is IInspectorLockAware lockAware)
            lockAware.SetLocked(value);
    }

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

        if (viewModel is IInspectorLockAware lockAware)
            lockAware.SetLocked(IsLocked);
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
