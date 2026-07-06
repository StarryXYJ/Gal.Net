using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Control.ViewModels;
using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.ViewModels;

public partial class GamePreviewPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Ready";

    public GamePageHostViewModel? PageHostVm { get; private set; }

    public ObservableCollection<string> OutputLines { get; } = [];
    public ObservableCollection<VariableItemViewModel> Variables { get; } = [];

    private readonly IServiceProvider _serviceProvider;

    public GamePreviewPanelViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Log.Information("[PreviewVM] .ctor thread={ThreadId}, isUI={IsUI}",
            Environment.CurrentManagedThreadId, Dispatcher.UIThread.CheckAccess());

        Task.Run(async () =>
        {
            await Task.Delay(100);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Log.Information("[PreviewVM] Creating GamePageHostViewModel...");
                var navService = _serviceProvider.GetRequiredService<INavigationService>();
                PageHostVm = new GamePageHostViewModel(navService, _serviceProvider);
                OnPropertyChanged(nameof(PageHostVm));
                Log.Information("[PreviewVM] GamePageHostViewModel created");
            });
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
