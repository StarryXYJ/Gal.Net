using System.Collections.ObjectModel;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Contracts.Builtins.Audio;

namespace GalNet.Avalonia.GameHost;

public sealed partial class AvaloniaPlayerSettingsService
{
    private readonly ObservableCollection<SettingCategory> _categories;
    private readonly IReadOnlyList<SettingItem> _baseAudioItems;
    private readonly int _audioRestoreStart;
    private static readonly AudioMixLevel NeutralMix = new();

    private void ConfigureAudio(AudioConfiguration configuration, bool showTracks)
    {
        _restoreValues.RemoveRange(_audioRestoreStart, _restoreValues.Count - _audioRestoreStart);
        var items = _baseAudioItems.ToList();
        foreach (var group in configuration.Buses) AddLevel(false, group.Id, group.Name, true);
        foreach (var track in configuration.Tracks) AddLevel(true, track.Id, track.Name, showTracks);
        var index = _categories.IndexOf(_categories.Single(category => category.Id == "audio"));
        _categories[index] = new SettingCategory("audio", "Settings.Audio", items, _localization.GetString("Settings.Audio"));
        _coordinator.Attach(Categories);

        void AddLevel(bool isTrack, string id, string name, bool visible)
        {
            var prefix = $"audio.{(isTrack ? "track" : "group")}.{Uri.EscapeDataString(id)}";
            var labelKey = string.IsNullOrWhiteSpace(name) ? id : name;
            var mute = Boolean(prefix + ".muted", "Settings.Muted", false,
                () => Level().Muted, value => Levels()[id] = Level() with { Muted = value });
            var gain = Number(prefix + ".gain", labelKey, 1, 1, .01,
                () => Level().Gain, value => Levels()[id] = Level() with { Gain = (float)value }, NumericEditorKind.Slider, livePreview: true, inlineToggle: mute);
            if (visible) { items.Add(gain); items.Add(mute); }
            Dictionary<string, AudioMixLevel> Levels() => isTrack ? RuntimeSettings.Tracks : RuntimeSettings.Buses;
            AudioMixLevel Level() => Levels().GetValueOrDefault(id) ?? NeutralMix;
        }
    }
}
