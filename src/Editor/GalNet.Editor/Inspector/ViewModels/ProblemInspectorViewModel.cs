using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed class ProblemInspectorViewModel : ObservableObject, IInspectorControlViewModel, IInspectorLockAware
{
    private readonly ProblemsPanelViewModel _page;
    private bool _locked;
    public AuthorProblem? Problem { get; private set; }
    public bool IsAvailable => Problem is not null;
    public ProblemInspectorViewModel(ProblemsPanelViewModel page)
    {
        _page = page;
        _page.PropertyChanged += Changed;
        Sync();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (!_locked && args.PropertyName == nameof(ProblemsPanelViewModel.SelectedProblem)) Sync();
    }
    private void Sync()
    {
        Problem = _page.SelectedProblem;
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(IsAvailable));
    }
    public void SetLocked(bool isLocked) { _locked = isLocked; if (!isLocked) Sync(); }
    public void Dispose() => _page.PropertyChanged -= Changed;
}
