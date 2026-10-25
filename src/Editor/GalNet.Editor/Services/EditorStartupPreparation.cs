using System;
using System.Threading.Tasks;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Two data-only tasks overlapped with platform initialization.</summary>
internal sealed class EditorStartupPreparation : IDisposable
{
    internal Task<EditorSettingsService> Settings { get; }
    internal Task<EditorLocalizationService.Resources> Localization { get; }

    internal EditorStartupPreparation(string? settingsPath = null)
    {
        Settings = Task.Run(() =>
        {
#if BENCHMARK
            using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.PrepareSettings");
#endif
            var settings = new EditorSettingsService(settingsPath);
            settings.GetSettings();
            return settings;
        });
        Localization = Task.Run(() =>
        {
#if BENCHMARK
            using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.PrepareLocalization");
#endif
            return EditorLocalizationService.Resources.Load();
        });
    }

    public void Dispose()
    {
        // Also observe both tasks if platform initialization fails before consumption.
        try { Task.WhenAll(Settings, Localization).GetAwaiter().GetResult(); }
        catch (Exception error) { Serilog.Log.Warning(error, "Startup data preparation failed"); }
    }
}
