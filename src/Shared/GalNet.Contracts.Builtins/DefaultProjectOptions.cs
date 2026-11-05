using GalNet.Core.I18n;

namespace GalNet.Contracts.Builtins;

public enum ProjectOrientationPolicy
{
    Unforced,
    ForceLandscape,
    ForcePortrait
}

public enum ProjectDefaultOrientation
{
    Landscape,
    Portrait
}

public sealed class DefaultProjectOptions
{
    public I18nLocale TargetLocale { get; set; } = I18nLocale.ZhCn;

    public List<I18nLocale> AvailableLocales { get; set; } = [I18nLocale.ZhCn];

    public int SaveSlotCount { get; set; } = 60;

    public List<GalNet.Contracts.Builtins.Audio.AudioBusDefinition> AudioBuses { get; set; } = [];
    public List<GalNet.Contracts.Builtins.Audio.AudioTrackDefinition> AudioTracks { get; set; } = [];

    public int DefaultWidth { get; set; } = 1920;

    public int DefaultHeight { get; set; } = 1080;

    public ProjectOrientationPolicy OrientationPolicy { get; set; } = ProjectOrientationPolicy.Unforced;

    public ProjectDefaultOrientation DefaultOrientation { get; set; } = ProjectDefaultOrientation.Landscape;
}
