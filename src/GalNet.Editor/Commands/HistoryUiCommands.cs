using System;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Sessions;

namespace GalNet.Editor.Commands;

public sealed class UndoEditorCommand : EditorUiCommand, IDisposable
{
    private readonly IEditorSession _session;
    private readonly RelayCommand _command;

    public override string Id => "editor.history.undo";
    public override string Description => "Undoes the most recent editor transaction in the current project session.";
    public override I18nKey DisplayNameKey { get; } = new("Editor.Menu.Undo");
    public override I18nKey CategoryKey { get; } = new("Command.Category.Edit");
    public override KeyGesture? DefaultGesture { get; } = new(Key.Z, KeyModifiers.Control);
    public override ICommand Command => _command;

    public UndoEditorCommand(IEditorSession session)
    {
        _session = session;
        _command = new RelayCommand(() => _session.Undo(), () => _session.CanUndo);
        Gesture = DefaultGesture;
        _session.HistoryChanged += OnHistoryChanged;
    }

    private void OnHistoryChanged() => _command.NotifyCanExecuteChanged();
    public void Dispose() => _session.HistoryChanged -= OnHistoryChanged;
}

public sealed class RedoEditorCommand : EditorUiCommand, IDisposable
{
    private readonly IEditorSession _session;
    private readonly RelayCommand _command;

    public override string Id => "editor.history.redo";
    public override string Description => "Redoes the next editor transaction in the current project session.";
    public override I18nKey DisplayNameKey { get; } = new("Editor.Menu.Redo");
    public override I18nKey CategoryKey { get; } = new("Command.Category.Edit");
    public override KeyGesture? DefaultGesture { get; } = new(Key.Y, KeyModifiers.Control);
    public override ICommand Command => _command;

    public RedoEditorCommand(IEditorSession session)
    {
        _session = session;
        _command = new RelayCommand(() => _session.Redo(), () => _session.CanRedo);
        Gesture = DefaultGesture;
        _session.HistoryChanged += OnHistoryChanged;
    }

    private void OnHistoryChanged() => _command.NotifyCanExecuteChanged();
    public void Dispose() => _session.HistoryChanged -= OnHistoryChanged;
}
