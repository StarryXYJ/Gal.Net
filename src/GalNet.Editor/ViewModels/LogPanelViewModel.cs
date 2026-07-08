using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public sealed partial class LogPanelViewModel : ObservableObject
{
    private readonly ObservableCollection<LogLine> _allLines;

    [ObservableProperty]
    private bool _onlyShowErrors;

    [ObservableProperty]
    private bool _autoScroll = true;

    public ObservableCollection<LogLine> Lines { get; } = [];

    public LogPanelViewModel()
    {
        _allLines = EditorLogSink.Instance.Lines;
        EditorLogSink.Instance.LineAdded += OnLineAdded;

        // Initial load
        RefreshFilter();
    }

    partial void OnOnlyShowErrorsChanged(bool value) => RefreshFilter();

    [RelayCommand]
    private void ToggleFilter()
    {
        OnlyShowErrors = !OnlyShowErrors;
    }

    public event Action? ScrollToEndRequested;

    private void OnLineAdded(LogLine line)
    {
        if (!OnlyShowErrors || line.IsError)
        {
            Lines.Add(line);
            if (AutoScroll)
                ScrollToEndRequested?.Invoke();
        }
    }

    private void RefreshFilter()
    {
        Lines.Clear();
        foreach (var line in _allLines)
        {
            if (!OnlyShowErrors || line.IsError)
                Lines.Add(line);
        }
    }
}