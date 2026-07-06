using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Input;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>
/// Manages editor commands and applies persisted shortcut overrides.
/// </summary>
public class CommandService
{
    private readonly Dictionary<Type, EditorCommand> _commands;
    private CommandConfig _config = new();

    public CommandService(IEnumerable<EditorCommand> commands)
    {
        _commands = commands.ToDictionary(command => command.GetType());
    }

    public T GetCommand<T>() where T : EditorCommand
    {
        var type = typeof(T);
        if (!_commands.TryGetValue(type, out var cmd))
            throw new InvalidOperationException($"Command {type.Name} is not registered in DI");

        ApplyGestureOverride((T)cmd);
        return (T)cmd;
    }

    public void LoadConfig(string path)
    {
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _config = JsonSerializer.Deserialize<CommandConfig>(json) ?? new CommandConfig();
        }
    }

    public void SaveConfig(string path)
    {
        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void SetGestureOverride(string commandId, string gestureText)
    {
        _config.GestureOverrides[commandId] = gestureText;
    }

    public CommandConfig GetConfig() => _config;

    private void ApplyGestureOverride<T>(T cmd) where T : EditorCommand
    {
        if (_config.GestureOverrides.TryGetValue(cmd.Id, out var gestureText))
        {
            try
            {
                cmd.Gesture = KeyGesture.Parse(gestureText);
            }
            catch
            {
                cmd.Gesture = cmd.DefaultGesture;
            }
        }
        else
        {
            cmd.Gesture = cmd.DefaultGesture;
        }
    }
}
