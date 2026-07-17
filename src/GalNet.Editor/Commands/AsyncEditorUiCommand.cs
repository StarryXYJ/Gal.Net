using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace GalNet.Editor.Commands;

public abstract class AsyncEditorUiCommand : EditorUiCommand
{
    private AsyncRelayCommand? _command;

    protected AsyncRelayCommand RelayCommand =>
        _command ?? throw new InvalidOperationException($"{GetType().Name}: InitializeCommand() has not been called.");

    public override ICommand Command => RelayCommand;

    protected void InitializeCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _command = canExecute is null
            ? new AsyncRelayCommand(execute)
            : new AsyncRelayCommand(execute, canExecute);
        Gesture = DefaultGesture;
    }
}
