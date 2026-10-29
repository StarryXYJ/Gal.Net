using GalNet.Core.I18n;

namespace GalNet.Contracts.Builtins;

public sealed class DefaultProjectOptions
{
    public I18nLocale TargetLocale { get; set; } = I18nLocale.ZhCn;

    public List<I18nLocale> AvailableLocales { get; set; } = [I18nLocale.ZhCn];

    public int SaveSlotCount { get; set; } = 60;

    public List<GalNet.Contracts.Builtins.Audio.AudioBusDefinition> AudioBuses { get; set; } = [];
    public List<GalNet.Contracts.Builtins.Audio.AudioTrackDefinition> AudioTracks { get; set; } = [];

    /// <summary>Authoring defaults only. Saved entries retain their explicit program resource IDs.</summary>
    public Dictionary<string, string> DefaultVisualPrograms { get; set; } = new(StringComparer.Ordinal);

    public int DefaultWidth { get; set; } = 1920;

    public int DefaultHeight { get; set; } = 1080;
}
