using System.Windows.Input;
using Avalonia.Input;
using GalNet.Core.I18n;
using GalNet.Editor.Abstraction.Commands;

namespace GalNet.Editor.Commands;

public interface IEditorUiCommandDefinition : IEditorCommandDefinition
{
    I18nKey CategoryKey { get; }
    string Context { get; }
    ICommand Command { get; }
}

public interface IEditorShortcutCommandDefinition : IEditorUiCommandDefinition
{
    KeyGesture? DefaultGesture { get; }
    KeyGesture? Gesture { get; set; }
}

public abstract class EditorUiCommand : IEditorShortcutCommandDefinition
{
    public abstract string Id { get; }
    public abstract string Description { get; }
    public abstract I18nKey DisplayNameKey { get; }
    public abstract I18nKey CategoryKey { get; }
    public virtual string Context => "Global";
    public abstract KeyGesture? DefaultGesture { get; }
    public KeyGesture? Gesture { get; set; }
    public abstract ICommand Command { get; }
}
