using GalNet.Core.I18n;
using GalNet.Core.Variable;

namespace GalNet.Core.Settings;

public sealed class ProjectSettings
{

    public I18nLocale TargetLocale { get; set; } = I18nLocale.ZhCn;

    public List<I18nLocale> AvailableLocales { get; set; } = [I18nLocale.ZhCn];

    public int SaveSlotCount { get; set; } = 60;

    public List<GalNet.Core.Audio.AudioGroupDefinition> AudioGroups { get; set; } = [];
    public List<GalNet.Core.Audio.AudioTrackDefinition> AudioTracks { get; set; } = [];

    public int DefaultWidth { get; set; } = 1920;

    public int DefaultHeight { get; set; } = 1080;

    public List<ProjectVariableDefinition> PlayerVariables { get; set; } = [];

    public List<ProjectVariableDefinition> SaveVariables { get; set; } = [];
}
