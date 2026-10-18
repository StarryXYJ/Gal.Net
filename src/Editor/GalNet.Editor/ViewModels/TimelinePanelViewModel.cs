using System;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.History;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public sealed partial class TimelinePanelViewModel(TimelineEditSession session, EditorHistories histories) : IUndoRedoTarget, IDisposable
{
    public TimelineEditSession Session { get; } = session;
    public IUndoRedoHistory UndoRedoHistory => histories.Content;
    [RelayCommand] private void Create() => Session.Create();
    public void Dispose() => Session.CancelInput();
}
