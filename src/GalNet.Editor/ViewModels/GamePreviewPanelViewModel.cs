using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Control.ViewModels;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class GamePreviewPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Ready";

    public GamePageHostViewModel? PageHostVm { get; private set; }

    public ObservableCollection<string> OutputLines { get; } = [];
    public ObservableCollection<VariableItemViewModel> Variables { get; } = [];

    private readonly GalNet.Core.Services.INavigationService _navigation;
    private readonly IGameFlowFactory _gameFlowFactory;
    private readonly EditorWorkspaceViewModel _workspace;

    public GamePreviewPanelViewModel(
        GalNet.Core.Services.INavigationService navigation,
        IGameFlowFactory gameFlowFactory,
        EditorWorkspaceViewModel workspace)
    {
        _navigation = navigation;
        _gameFlowFactory = gameFlowFactory;
        _workspace = workspace;
        Log.Information("[PreviewVM] .ctor thread={ThreadId}, isUI={IsUI}",
            Environment.CurrentManagedThreadId, Dispatcher.UIThread.CheckAccess());

        Variables.Add(new VariableItemViewModel { Name = "player.name", TypeName = "string", Value = "Alice" });
        Variables.Add(new VariableItemViewModel { Name = "save.route", TypeName = "string", Value = "opening" });
        Variables.Add(new VariableItemViewModel { Name = "flag.firstChoice", TypeName = "bool", Value = "false" });

        _ = RestartPreviewAsync();
    }

    public void FocusInspector() => _workspace.FocusPreview(this);

    [RelayCommand]
    private async Task RestartPreviewAsync()
    {
        StatusText = "Restarting...";
        await Task.Delay(100);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Log.Information("[PreviewVM] Creating GamePageHostViewModel...");
            PageHostVm = _gameFlowFactory.CreatePageHost(_navigation);
            OnPropertyChanged(nameof(PageHostVm));
            OutputLines.Insert(0, $"Preview restarted at {DateTime.Now:T}");
            StatusText = "Running";
            Log.Information("[PreviewVM] GamePageHostViewModel created");
        });
    }
}

public partial class VariableItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _typeName = "";

    [ObservableProperty]
    private string _value = "";

    public event Action<string, string>? ValueChanged;

    partial void OnValueChanged(string value)
    {
        ValueChanged?.Invoke(Name, value);
    }
}
