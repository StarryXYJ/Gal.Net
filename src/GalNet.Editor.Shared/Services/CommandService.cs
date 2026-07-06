using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Input;
using GalNet.Editor.Shared.Commands;

namespace GalNet.Editor.Shared.Services;

/// <summary>
/// 命令管理器 —— 提供泛型工厂 GetCommand&lt;T&gt;() 从 DI 解析命令，
/// 并支持快捷键配置的序列化保存/加载。
/// </summary>
public class CommandService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, EditorCommand> _cache = new();
    private CommandConfig _config = new();

    public CommandService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>获取指定类型的命令实例（缓存单例）</summary>
    public T GetCommand<T>() where T : EditorCommand
    {
        var type = typeof(T);
        if (!_cache.TryGetValue(type, out var cmd))
        {
            cmd = (T)(_serviceProvider.GetService(type)
                ?? throw new InvalidOperationException($"Command {type.Name} is not registered in DI"));
            _cache[type] = cmd;
        }

        // 每次获取时重新应用快捷键覆盖（允许运行时热更新配置）
        ApplyGestureOverride((T)cmd);
        return (T)cmd;
    }

    /// <summary>加载快捷键配置</summary>
    public void LoadConfig(string path)
    {
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _config = JsonSerializer.Deserialize<CommandConfig>(json) ?? new CommandConfig();
        }
    }

    /// <summary>保存快捷键配置</summary>
    public void SaveConfig(string path)
    {
        var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>设置某个命令的快捷键覆盖</summary>
    public void SetGestureOverride(string commandId, string gestureText)
    {
        _config.GestureOverrides[commandId] = gestureText;
    }

    /// <summary>获取当前配置（供外部编辑）</summary>
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
                // 解析失败时回退到默认快捷键
                cmd.Gesture = cmd.DefaultGesture;
            }
        }
        else
        {
            cmd.Gesture = cmd.DefaultGesture;
        }
    }
}
