using System.Text.Json;
using GalNet.Core.Services;
using GalNet.Core.UI;

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
    }

    private UiProject Load(UiProject defaults)
    {
        if (!File.Exists(_path)) return defaults;
        try { return JsonSerializer.Deserialize<UiProject>(File.ReadAllText(_path), Options) ?? defaults; }
        catch (JsonException) { return defaults; }
    }
}

public static class UiProjectDefaults
{
    public static UiProject Create() => new();
}
