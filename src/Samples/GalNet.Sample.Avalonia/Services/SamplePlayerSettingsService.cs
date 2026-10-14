using DynamicLocalization.Core;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Settings;
using GalNet.Core.Variable;
using GalNet.Game.Controls.Forms;
using GalNet.Runtime.Variables;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Applies preferences and persists them through the session's player variables.</summary>
public sealed class SamplePlayerSettingsService : IPlayerSettingsService, IDisposable
{
    public const string LocaleVariable = "player.galnet.settings.locale";
    public const string TextSpeedVariable = "player.galnet.settings.textSpeed";
    private readonly SampleLocalizationService _localization;
    private readonly GamePageViewModel _gameplay;
    private readonly ChoiceSetting<string> _language;
    private readonly SettingsCoordinator _coordinator;
    private readonly IPlayerDisplayService _display;
    private readonly ChoiceSetting<PlayerDisplayMode> _displayMode;
    private readonly ChoiceSetting<PlayerWindowSize> _windowSize;
    private PlayerVariableService? _variables;
    private bool _restoring;
    private readonly List<Action<IReadOnlyDictionary<string, GalNet.Core.Variable.Variable>>> _restoreValues = [];
    public GameSettings RuntimeSettings { get; private set; } = new();
    public event Action<GameSettings>? SettingsChanged;

    public SamplePlayerSettingsService(SampleLocalizationService localization, GamePageViewModel gameplay, IPlayerDisplayService? display = null)
    {
        _localization = localization;
        _gameplay = gameplay;
        _display = display ?? new SampleWindowDisplayService();
        _displayMode = new ChoiceSetting<PlayerDisplayMode>("displayMode", "Settings.DisplayMode", PlayerDisplayMode.Window,
            _display.Modes.Select(mode => new SettingChoice<PlayerDisplayMode>(mode.ToString(), "Settings.Display." + mode, mode, localization.GetString("Settings.Display." + mode))),
            () => _display.Mode, async (value, token) => { await _display.ApplyModeAsync(value, token); Store("player.galnet.settings.displayMode", value.ToString()); }, localization.GetString("Settings.DisplayMode"))
        { EvaluateAvailability = () => new(_display.IsAvailable) };
        _windowSize = new ChoiceSetting<PlayerWindowSize>("windowSize", "Settings.WindowSize", new(1280, 720),
            _display.WindowSizes.Select(size => new SettingChoice<PlayerWindowSize>($"{size.Width}x{size.Height}", $"{size.Width} × {size.Height}", size)),
            () => _display.WindowSize, async (value, token) => { await _display.ApplyWindowSizeAsync(value, token); Store("player.galnet.settings.windowSize", $"{value.Width}x{value.Height}"); }, localization.GetString("Settings.WindowSize"))
        { EvaluateAvailability = () => new(_display.IsAvailable, _display.Mode == PlayerDisplayMode.Window, _display.Mode == PlayerDisplayMode.Window ? null : "Settings.WindowSizeFullscreen") };
        _language = new ChoiceSetting<string>("locale", "Settings.Language", localization.CultureService.CurrentCulture.Name,
            Languages(), () => localization.CultureService.CurrentCulture.Name,
            (value, _) => { localization.ApplyCulture(value); Store(LocaleVariable, value); return Task.CompletedTask; },
            localization.GetString("Settings.Language"));
        Categories = Array.AsReadOnly(new[]
        {
            new SettingCategory("game", "Settings.Game",
            [
                _language,
                Number("textSpeed", "Settings.TextSpeed", 30, 120, 1, () => RuntimeSettings.TextSpeed,
                    value => { RuntimeSettings.TextSpeed = (float)value; gameplay.TextSpeed = value; }, NumericEditorKind.Slider),
                Boolean("quickReadOnly", "Settings.QuickReadOnly", true, () => RuntimeSettings.QuickReadOnly, value => RuntimeSettings.QuickReadOnly = value),
                Milliseconds("quickInterval", "Settings.QuickInterval", 50, 5000, 10, () => RuntimeSettings.QuickAdvanceIntervalMilliseconds, value => RuntimeSettings.QuickAdvanceIntervalMilliseconds = value),
                Milliseconds("autoInterval", "Settings.AutoInterval", 2000, 30000, 100, () => RuntimeSettings.AutoAdvanceIntervalMilliseconds, value => RuntimeSettings.AutoAdvanceIntervalMilliseconds = value)
            ], localization.GetString("Settings.Game")),
            new SettingCategory("graphics", "Settings.Graphics", [_displayMode, _windowSize], localization.GetString("Settings.Graphics")),
            new SettingCategory("audio", "Settings.Audio",
            [
                Number("masterVolume", "Settings.MasterVolume", 1, 1, .01, () => RuntimeSettings.MasterVolume, value => RuntimeSettings.MasterVolume = (float)value, NumericEditorKind.Slider),
                Boolean("masterMuted", "Settings.MasterMuted", false, () => RuntimeSettings.MasterMuted, value => RuntimeSettings.MasterMuted = value),
                Number("bgmVolume", "Settings.BgmVolume", 1, 1, .01, () => RuntimeSettings.BgmVolume, value => RuntimeSettings.BgmVolume = (float)value, NumericEditorKind.Slider),
                Boolean("bgmMuted", "Settings.BgmMuted", false, () => RuntimeSettings.BgmMuted, value => RuntimeSettings.BgmMuted = value),
                Number("sfxVolume", "Settings.SfxVolume", 1, 1, .01, () => RuntimeSettings.SfxVolume, value => RuntimeSettings.SfxVolume = (float)value, NumericEditorKind.Slider),
                Boolean("sfxMuted", "Settings.SfxMuted", false, () => RuntimeSettings.SfxMuted, value => RuntimeSettings.SfxMuted = value),
                Number("voiceVolume", "Settings.VoiceVolume", 1, 1, .01, () => RuntimeSettings.VoiceVolume, value => RuntimeSettings.VoiceVolume = (float)value, NumericEditorKind.Slider),
                Boolean("voiceMuted", "Settings.VoiceMuted", false, () => RuntimeSettings.VoiceMuted, value => RuntimeSettings.VoiceMuted = value)
            ], localization.GetString("Settings.Audio"))
        });
        _coordinator = new SettingsCoordinator { SaveAsync = token => _variables?.FlushPlayerVariablesAsync(token) ?? Task.CompletedTask };
        _coordinator.Attach(Categories);
        localization.CultureService.CultureChanged += OnCultureChanged;
        _display.Changed += OnDisplayChanged;
    }

    public IReadOnlyList<SettingCategory> Categories { get; }
    public EditorRegistry Editors { get; } = EditorRegistry.CreateDefault();
    public Task FlushAsync() => _coordinator.FlushAsync();

    public void BindWindow(global::Avalonia.Controls.Window? window)
    {
        if (_display is SampleWindowDisplayService desktop) desktop.Bind(window);
    }

    public async Task RestoreDisplayAsync(CancellationToken cancellationToken = default)
    {
        if (!_display.IsAvailable || _variables is null) return;
        var saved = _variables.GetSnapshot(VariableScope.Player);
        var mode = _displayMode.DefaultValue;
        if (saved.TryGetValue("player.galnet.settings.displayMode", out var modeValue) && modeValue.Type == VariableType.String)
            mode = _displayMode.Choices.FirstOrDefault(choice => choice.Id == modeValue.AsString())?.Value ?? mode;
        var size = _windowSize.DefaultValue;
        if (saved.TryGetValue("player.galnet.settings.windowSize", out var sizeValue) && sizeValue.Type == VariableType.String)
            size = _windowSize.Choices.FirstOrDefault(choice => choice.Id == sizeValue.AsString())?.Value ?? size;
        // Restore remembered window dimensions before entering fullscreen; no preference writes.
        await _display.ApplyModeAsync(PlayerDisplayMode.Window, cancellationToken);
        await _display.ApplyWindowSizeAsync(size, cancellationToken);
        await _display.ApplyModeAsync(mode, cancellationToken);
        OnDisplayChanged();
    }

    private void OnDisplayChanged()
    {
        _displayMode.Refresh();
        _windowSize.Refresh();
        _coordinator.RefreshAvailability();
    }

    public async Task AttachAsync(PlayerVariableService variables, GameSettings runtimeSettings, CancellationToken cancellationToken = default)
    {
        _variables = variables;
        RuntimeSettings = runtimeSettings;
        _restoring = true;
        try
        {
            var stored = variables.GetSnapshot(VariableScope.Player);
            var languages = Languages();
            if (stored.TryGetValue(LocaleVariable, out var locale) && locale.Type == VariableType.String && languages.Any(option => option.Value == locale.AsString()))
                _localization.ApplyCulture(locale.AsString());
            foreach (var restore in _restoreValues) restore(stored);
            _language.DefaultValue = _localization.CultureService.CurrentCulture.Name;
            await _language.UpdateOptionsAsync(languages, cancellationToken);
        }
        finally
        {
            _restoring = false;
            foreach (var setting in Categories.SelectMany(category => category.Items)) setting.Refresh();
            _coordinator.RefreshAvailability();
        }
    }

    private DoubleSetting Number(string id, string key, double defaultValue, double maximum, double step,
        Func<double> read, Action<double> write, NumericEditorKind editor = NumericEditorKind.NumericUpDown)
    {
        var variable = "player.galnet.settings." + id;
        _restoreValues.Add(stored =>
        {
            var value = stored.TryGetValue(variable, out var saved) && saved.Type == VariableType.Float ? saved.AsFloat() : defaultValue;
            write(double.IsFinite(value) && value >= 0 && value <= maximum ? value : defaultValue);
        });
        return new DoubleSetting(id, key, defaultValue, 0, maximum, step, read,
            (value, token) => ApplyRuntimeSettingAsync(variable, () => write(value), (float)value, token), _localization.GetString(key))
        { EditorKind = editor };
    }

    private IntSetting Milliseconds(string id, string key, int defaultValue, int maximum, int step, Func<int> read, Action<int> write)
    {
        var variable = "player.galnet.settings." + id;
        _restoreValues.Add(stored =>
        {
            var value = stored.TryGetValue(variable, out var saved) && saved.Type == VariableType.Int ? saved.AsInt() : defaultValue;
            write(value >= 0 && value <= maximum ? value : defaultValue);
        });
        return new IntSetting(id, key, defaultValue, 0, maximum, step, read,
            (value, token) => ApplyRuntimeSettingAsync(variable, () => write(value), value, token), _localization.GetString(key))
        { EditorKind = NumericEditorKind.NumericUpDown, Format = "0" };
    }

    private BoolSetting Boolean(string id, string key, bool defaultValue, Func<bool> read, Action<bool> write)
    {
        var variable = "player.galnet.settings." + id;
        _restoreValues.Add(stored => write(stored.TryGetValue(variable, out var saved) && saved.Type == VariableType.Bool ? saved.AsBool() : defaultValue));
        return new BoolSetting(id, key, defaultValue, read,
            (value, token) => ApplyRuntimeSettingAsync(variable, () => write(value), value, token), _localization.GetString(key));
    }

    private async Task ApplyRuntimeSettingAsync(string variable, Action apply, object storedValue, CancellationToken cancellationToken)
    {
        apply();
        SettingsChanged?.Invoke(RuntimeSettings);
        await _gameplay.RefreshPlaybackSettingsAsync(cancellationToken);
        Store(variable, storedValue);
    }

    private void OnCultureChanged(object? sender, CultureChangedEventArgs args) => _language.Refresh();

    private SettingChoice<string>[] Languages() => _localization.CultureService.AvailableCultures
        .OrderBy(culture => culture.Name, StringComparer.Ordinal)
        .Select(culture => new SettingChoice<string>(culture.Name, culture.Name, culture.Name, culture.NativeName)).ToArray();

    private void Store(string name, object value)
    {
        if (_restoring || _variables is not { } variables) return;
        var variable = new GalNet.Core.Variable.Variable { Name = name };
        variable.SetValue(value);
        variables.NotifyVariableChanged(VariableScope.Player, name, variable);
    }

    public void Dispose()
    {
        _localization.CultureService.CultureChanged -= OnCultureChanged;
        _display.Changed -= OnDisplayChanged;
        if (_display is SampleWindowDisplayService desktop) desktop.Dispose();
    }
}
