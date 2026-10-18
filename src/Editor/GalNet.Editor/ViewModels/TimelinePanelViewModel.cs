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
    [RelayCommand] private void AddKey() => Session.AddCurrentKey();
    [RelayCommand] private void DeleteKeys() => Session.DeleteSelectedKeys();
    [RelayCommand] private void PreviousKey() => Session.NavigateKey(-1);
    [RelayCommand] private void NextKey() => Session.NavigateKey(1);
    [RelayCommand] private void ApplyTiming() => Session.ApplyTiming(false);
    [RelayCommand] private void TrimTiming() => Session.ApplyTiming(true);
    [RelayCommand] private void ReplaceCollisions() => Session.ReplaceKeyCollisions();
    [RelayCommand] private void CancelMove() => Session.Canvas.Cancel();
    public void Dispose() => Session.CancelInput();
}
