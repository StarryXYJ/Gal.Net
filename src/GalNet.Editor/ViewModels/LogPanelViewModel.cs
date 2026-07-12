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

    [ObservableProperty]
    private LogChannel _channel = LogChannel.Game;

    public ObservableCollection<LogLine> Lines { get; } = [];
    public bool IsGameEnabled { get => (Channel & LogChannel.Game) != 0; set => Channel = value ? Channel | LogChannel.Game : Channel & ~LogChannel.Game; }
    public bool IsEditorEnabled { get => (Channel & LogChannel.Editor) != 0; set => Channel = value ? Channel | LogChannel.Editor : Channel & ~LogChannel.Editor; }
    public string ChannelDisplay => Channel switch
    {
        LogChannel.Game => "Game",
        LogChannel.Editor => "Editor",
        LogChannel.Game | LogChannel.Editor => "Game, Editor",
        _ => "None"
    };

    public LogPanelViewModel()
    {
        _allLines = EditorLogSink.Instance.Lines;
        EditorLogSink.Instance.LineAdded += OnLineAdded;

        // Initial load
        RefreshFilter();
    }

    partial void OnOnlyShowErrorsChanged(bool value) => RefreshFilter();
    partial void OnChannelChanged(LogChannel value)
    {
        OnPropertyChanged(nameof(IsGameEnabled));
        OnPropertyChanged(nameof(IsEditorEnabled));
        OnPropertyChanged(nameof(ChannelDisplay));
        RefreshFilter();
    }

    [RelayCommand]
    private void ToggleFilter()
    {
        OnlyShowErrors = !OnlyShowErrors;
    }

    public event Action? ScrollToEndRequested;

    private void OnLineAdded(LogLine line)
    {
        if (Matches(line))
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
            if (Matches(line))
                Lines.Add(line);
        }
    }

    private bool Matches(LogLine line) => (Channel & line.Channel) != 0
        && (!OnlyShowErrors || line.IsError);
}
