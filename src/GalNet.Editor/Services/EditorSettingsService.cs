using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GalNet.Core.I18n;
using GalNet.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Services;

/// <summary>
/// 编辑器设置管理 —— 读取/保存 EditorSettings 到 AppData。
/// </summary>
public sealed class EditorSettingsService : IEditorSettingsService
{
    private readonly string _settingsPath;
    private EditorSettings? _settings;

    public EditorSettingsService()
    {
        _settingsPath = Path.Combine(
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

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save editor settings");
        }
    }
}
