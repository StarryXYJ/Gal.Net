using System;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.I18n;
using GalNet.Editor.History;

namespace GalNet.Editor.Commands;

public sealed class UndoEditorCommand : EditorUiCommand, IDisposable
{
    private readonly UndoRedoRouter _router;
    private readonly RelayCommand _command;

    public override string Id => "editor.history.undo";
    public override string Description => "Undoes the most recent edit in the active editor domain.";
    public override I18nKey DisplayNameKey { get; } = new("Editor.Menu.Undo");
    public override I18nKey CategoryKey { get; } = new("Command.Category.Edit");
    public override KeyGesture? DefaultGesture { get; } = new(Key.Z, KeyModifiers.Control);
    public override ICommand Command => _command;

    public UndoEditorCommand(UndoRedoRouter router)
    {
        _router = router;
        _command = new RelayCommand(_router.Undo, () => _router.Active?.CanUndo == true);
        Gesture = DefaultGesture;
        _router.Changed += OnHistoryChanged;
    }

    private void OnHistoryChanged() => _command.NotifyCanExecuteChanged();
    public void Dispose() => _router.Changed -= OnHistoryChanged;
}

public sealed class RedoEditorCommand : EditorUiCommand, IDisposable
{
    private readonly UndoRedoRouter _router;
    private readonly RelayCommand _command;

    public override string Id => "editor.history.redo";
    public override string Description => "Redoes the next edit in the active editor domain.";
    public override I18nKey DisplayNameKey { get; } = new("Editor.Menu.Redo");
    public override I18nKey CategoryKey { get; } = new("Command.Category.Edit");
    public override KeyGesture? DefaultGesture { get; } = new(Key.Y, KeyModifiers.Control);
    public override ICommand Command => _command;

    public RedoEditorCommand(UndoRedoRouter router)
    {
        _router = router;
        _command = new RelayCommand(_router.Redo, () => _router.Active?.CanRedo == true);
        Gesture = DefaultGesture;
        _router.Changed += OnHistoryChanged;
    }

    private void OnHistoryChanged() => _command.NotifyCanExecuteChanged();
    public void Dispose() => _router.Changed -= OnHistoryChanged;
}
