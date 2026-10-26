using GalNet.Core.I18n;

namespace GalNet.Contracts.Builtins;

public sealed class DefaultProjectOptions
{
    public I18nLocale TargetLocale { get; set; } = I18nLocale.ZhCn;

    public List<I18nLocale> AvailableLocales { get; set; } = [I18nLocale.ZhCn];

    public int SaveSlotCount { get; set; } = 60;

    public List<GalNet.Core.Audio.AudioGroupDefinition> AudioGroups { get; set; } = [];
    public List<GalNet.Core.Audio.AudioTrackDefinition> AudioTracks { get; set; } = [];

    public int DefaultWidth { get; set; } = 1920;

    public int DefaultHeight { get; set; } = 1080;
}
