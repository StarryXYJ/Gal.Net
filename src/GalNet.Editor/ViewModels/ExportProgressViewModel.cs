using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.ViewModels;

public sealed partial class ExportProgressViewModel(IEditorLocalizationService localization, Action cancel) : ObservableObject
{
    public IEditorLocalizationService L { get; } = localization;

    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private string? _error;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    [RelayCommand]
    private void Cancel() => cancel();

    public void CompleteFailure(string error)
    {
        Error = error;
        IsRunning = false;
    }

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
