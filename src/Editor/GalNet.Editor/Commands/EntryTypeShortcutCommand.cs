using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Core.I18n;

namespace GalNet.Editor.Commands;

public sealed class EntryTypeShortcutCommand : EditorUiCommand
{
    private readonly RelayCommand _command = new(() => { });
    private readonly EntryDefinition _definition;

    public string EntryType => _definition.Type;
    public override string Id => $"entry.type.{_definition.Type}";
    public override string Description => $"Selects the {_definition.Type} entry type while the type picker is focused.";
    public override I18nKey DisplayNameKey { get; }
    public override I18nKey CategoryKey { get; } = new("Command.Category.EntryType");
    public override string Context => "EntryTypePicker";
    public override KeyGesture? DefaultGesture { get; }
    public override ICommand Command => _command;

    public EntryTypeShortcutCommand(EntryDefinition definition)
    {
        _definition = definition;
        DisplayNameKey = new I18nKey($"Entry.Type.{definition.Type}");
        DefaultGesture = definition.Type switch
        {
            BuiltinEntryIds.Text => new KeyGesture(Key.T),
            BuiltinEntryIds.ShowLayer => new KeyGesture(Key.L),
            BuiltinEntryIds.HideLayer => new KeyGesture(Key.L, KeyModifiers.Shift),
            BuiltinEntryIds.MoveLayer => new KeyGesture(Key.L, KeyModifiers.Control),
            _ => null
        };
        Gesture = DefaultGesture;
    }
}
