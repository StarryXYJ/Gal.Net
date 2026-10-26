using GalNet.Editor.Abstraction.Settings;
using System;
using System.IO;
using System.Text.Json;
using GalNet.Editor.Abstraction.Services;
using Serilog;

namespace GalNet.Editor.Shared.Services;

/// <summary>
/// 编辑器设置管理 —— 读取/保存 EditorSettings 到 AppData。
/// </summary>
public sealed class EditorSettingsService : IEditorSettingsService
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private EditorSettings? _settings;

    public EditorSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GalNet", "editor-settings.json");
    }

    public EditorSettings GetSettings()
    {
        if (_settings != null) return _settings;
        _settings = Load();
        return _settings;
    }

    public void SaveSettings()
    {
        if (_settings == null) return;
        Save(_settings);
    }

    private EditorSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<EditorSettings>(json) ?? new EditorSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load editor settings, using defaults");
        }
        return new EditorSettings();
    }

    private void Save(EditorSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, IndentedJsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save editor settings");
        }
    }
}
