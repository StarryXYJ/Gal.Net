using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Commands;

public sealed class EditorShortcutService
{
    private readonly IReadOnlyList<IEditorShortcutCommandDefinition> _commands;
    private readonly IEditorSettingsService _settingsService;

    public IReadOnlyList<IEditorShortcutCommandDefinition> Commands => _commands;
    public event Action? ShortcutsChanged;

    public EditorShortcutService(
        IEnumerable<IEditorShortcutCommandDefinition> commands,
        IEditorSettingsService settingsService)
    {
        _commands = commands.OrderBy(command => command.Id, StringComparer.Ordinal).ToList();
        _settingsService = settingsService;
        ApplyOverrides();
    }

    public T GetCommand<T>() where T : class, IEditorShortcutCommandDefinition =>
        _commands.OfType<T>().SingleOrDefault()
        ?? throw new InvalidOperationException($"UI command {typeof(T).Name} is not registered.");

    public void SetGesture(string commandId, KeyGesture? gesture)
    {
        var command = Find(commandId);
        if (gesture is not null)
        {
            var conflict = FindConflict(commandId, command.Context, gesture);
            if (conflict is not null)
                throw new InvalidOperationException($"Shortcut '{gesture}' is already assigned to '{conflict.Id}' in context '{command.Context}'.");
        }

        command.Gesture = gesture;
        var settings = _settingsService.GetSettings();
        settings.GestureOverrides[command.Id] = gesture?.ToString() ?? "";
        _settingsService.SaveSettings();
        ShortcutsChanged?.Invoke();
    }

    public void ResetGesture(string commandId)
    {
        var command = Find(commandId);
        command.Gesture = command.DefaultGesture;
        _settingsService.GetSettings().GestureOverrides.Remove(command.Id);
        _settingsService.SaveSettings();
        ShortcutsChanged?.Invoke();
    }

    public void ResetAll()
    {
        _settingsService.GetSettings().GestureOverrides.Clear();
        foreach (var command in _commands)
            command.Gesture = command.DefaultGesture;
        _settingsService.SaveSettings();
        ShortcutsChanged?.Invoke();
    }

    public IEditorShortcutCommandDefinition? FindConflict(
        string commandId,
        string context,
        KeyGesture gesture) =>
        _commands.FirstOrDefault(command =>
            command.Id != commandId &&
            command.Context.Equals(context, StringComparison.OrdinalIgnoreCase) &&
            command.Gesture?.Equals(gesture) == true);

    public bool TryExecute(KeyGesture gesture, string context = "Global")
    {
        var command = _commands.FirstOrDefault(candidate =>
            (candidate.Context.Equals("Global", StringComparison.OrdinalIgnoreCase) ||
             candidate.Context.Equals(context, StringComparison.OrdinalIgnoreCase)) &&
            candidate.Gesture?.Equals(gesture) == true &&
            candidate.Command.CanExecute(null));
        if (command is null) return false;
        command.Command.Execute(null);
        return true;
    }

    public IEditorShortcutCommandDefinition? FindByGesture(KeyGesture gesture, string context) =>
        _commands.FirstOrDefault(candidate =>
            candidate.Context.Equals(context, StringComparison.OrdinalIgnoreCase) &&
            candidate.Gesture?.Equals(gesture) == true);

    private IEditorShortcutCommandDefinition Find(string commandId) =>
        _commands.FirstOrDefault(command => command.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"UI command '{commandId}' is not registered.");

    private void ApplyOverrides()
    {
        var overrides = _settingsService.GetSettings().GestureOverrides;
        foreach (var command in _commands)
        {
            command.Gesture = command.DefaultGesture;
            if (!overrides.TryGetValue(command.Id, out var text)) continue;
            if (string.IsNullOrWhiteSpace(text)) command.Gesture = null;
            else
            {
                try { command.Gesture = KeyGesture.Parse(text); }
                catch { command.Gesture = command.DefaultGesture; }
            }
        }
    }
}
