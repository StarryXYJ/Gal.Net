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

        Update(command, gesture, removeOverride: false);
    }

    public void ResetGesture(string commandId)
    {
        var command = Find(commandId);
        if (command.DefaultGesture is { } gesture && FindConflict(commandId, command.Context, gesture) is { } conflict)
            throw new InvalidOperationException($"Shortcut '{gesture}' is already assigned to '{conflict.Id}'.");
        Update(command, command.DefaultGesture, removeOverride: true);
    }

    private void Update(IEditorShortcutCommandDefinition command, KeyGesture? gesture, bool removeOverride)
    {
        var settings = _settingsService.GetSettings();
        var hadOverride = settings.GestureOverrides.TryGetValue(command.Id, out var previous);
        if (removeOverride) settings.GestureOverrides.Remove(command.Id);
        else settings.GestureOverrides[command.Id] = gesture?.ToString() ?? "";
        try { _settingsService.SaveSettings(); }
        catch
        {
            if (hadOverride) settings.GestureOverrides[command.Id] = previous!;
            else settings.GestureOverrides.Remove(command.Id);
            throw;
        }
        command.Gesture = gesture;
        ShortcutsChanged?.Invoke();
    }

    public void ResetAll()
    {
        var settings = _settingsService.GetSettings();
        var previous = settings.GestureOverrides.ToArray();
        settings.GestureOverrides.Clear();
        try { _settingsService.SaveSettings(); }
        catch { foreach (var pair in previous) settings.GestureOverrides[pair.Key] = pair.Value; throw; }
        foreach (var command in _commands) command.Gesture = command.DefaultGesture;
        ShortcutsChanged?.Invoke();
    }

    public IEditorShortcutCommandDefinition? FindConflict(
        string commandId, string context, KeyGesture gesture) =>
        _commands.FirstOrDefault(command =>
            command.Id != commandId &&
            (command.Context.Equals(context, StringComparison.OrdinalIgnoreCase) ||
             command.Context.Equals("Global", StringComparison.OrdinalIgnoreCase) ||
             context.Equals("Global", StringComparison.OrdinalIgnoreCase)) &&
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
