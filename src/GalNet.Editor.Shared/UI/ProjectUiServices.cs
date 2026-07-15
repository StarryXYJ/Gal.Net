using System.Text.Json;
using GalNet.Core.Services;
using GalNet.Core.UI;
using Serilog;

namespace GalNet.Editor.Shared.UI;

/// <summary>Editor-owned persistence for the single built-in UI configuration document.</summary>
public sealed class FileUiProjectProvider : IUiProjectProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new ColorJsonConverter() }
    };
    private readonly string _path;
    public UiProject Current { get; private set; }
    public event Action? Changed;

    public FileUiProjectProvider(string projectRoot, UiProject? defaults = null)
    {
        _path = Path.Combine(projectRoot, "UI", "ui.json");
        Current = Load(defaults ?? UiProjectDefaults.Create());
    }

    public void Replace(UiProject project) { Current = project; NotifyChanged(); }
    public void NotifyChanged() => Changed?.Invoke();
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(Current, Options), cancellationToken);
        Log.Information("Saved UI project {Path}: version={Version}, pages={PageCount}", _path, Current.Version, Current.Pages.Count);
    }

    private UiProject Load(UiProject defaults)
    {
        if (!File.Exists(_path))
        {
            Log.Information("UI project file does not exist; using defaults: {Path}", _path);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var project = JsonSerializer.Deserialize<UiProject>(json, Options);
            if (project is null)
            {
                Log.Warning("UI project deserialized to null; using defaults: {Path}", _path);
                return defaults;
            }

            Log.Information("Loaded UI project {Path}: bytes={ByteCount}, version={Version}, pages={PageCount}, titlePreset={TitlePreset}, titleSettings={TitleSettings}, titleValues={@TitleValues}",
                _path, json.Length, project.Version, project.Pages.Count,
                project.GetPage(UiPageKind.Title).PresetId, project.GetPage(UiPageKind.Title).Settings.Count, project.GetPage(UiPageKind.Title).Settings);
            return project;
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse UI project; using defaults: {Path}", _path);
            return defaults;
        }
    }
}

public static class UiProjectDefaults
{
    public static UiProject Create() => new();
}
