using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public sealed class LogPanelViewModel : ObservableObject
{
    public ObservableCollection<string> Lines => EditorLogSink.Instance.Lines;
}
