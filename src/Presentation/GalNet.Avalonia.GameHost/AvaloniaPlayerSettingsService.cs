using Avalonia.Threading;
using DynamicLocalization.Core;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Variable;
using GalNet.Game.Controls.Forms;
using GalNet.GameHost;
using GalNet.Runtime.Logging;
using GalNet.Runtime.Variables;

namespace GalNet.Avalonia.GameHost;

/// <summary>Applies preferences and persists them through the session's player variables.</summary>
public sealed partial class AvaloniaPlayerSettingsService : IPlayerSettingsService, IDisposable
{
    public const string LocaleVariable = PlayerPreferences.LocaleVariable;
    public const string TextSpeedVariable = PlayerPreferences.TextSpeedVariable;
    private readonly IPlayerLocalization _localization;
    private readonly GamePageViewModel _gameplay;
    private readonly ChoiceSetting<string> _language;
    private readonly SettingsCoordinator _coordinator;
    private readonly DispatcherTimer _audioPreviewTimer;
    private readonly DispatcherTimer _liveSaveTimer;
    private readonly IPlayerDisplayService _display;
    private readonly ChoiceSetting<PlayerDisplayMode> _displayMode;
    private readonly ChoiceSetting<PlayerWindowSize> _windowSize;
    private PlayerVariableService? _variables;
    private PlayerPreferences? _preferences;
    private bool _restoring;
    private bool _audioPreviewPending;
    private bool _liveSavePending;
    private long _liveSaveRevision;
    private readonly List<Action<IReadOnlyDictionary<string, GalNet.Core.Variable.Variable>>> _restoreValues = [];
    public GameSettings RuntimeSettings { get; private set; } = new();
    public event Action<GameSettings>? SettingsChanged;
    public PlayerInputSettings Input { get; } = new();
    private readonly CustomSetting<PlayerInputConfiguration> _inputSetting;
    private readonly AvaloniaGamepadInput _gamepadInput;

    public AvaloniaPlayerSettingsService(IPlayerLocalization localization, GamePageViewModel gameplay, IPlayerDisplayService? display = null, EditorRegistry? editors = null)
    {
        Editors = editors ?? EditorRegistry.CreateDefault();
        _localization = localization;
        _gameplay = gameplay;
        gameplay.Input = Input;
        _gamepadInput = new AvaloniaGamepadInput(Input);
        _inputSetting = new CustomSetting<PlayerInputConfiguration>("inputBindings", "Settings.Operation", PlayerInputSettings.Defaults(),
            () => Input.Configuration, (value, _) =>
            {
                Input.Apply(value);
                Store("player.galnet.input.bindings", Input.Serialize());
                return Task.CompletedTask;
            }, _ =>
            {
                var panel = new InputBindingsPanel(Input);
                return new FieldEditorLease(panel, panel.Dispose);
            }, localization.GetString("Settings.Operation"))
        { IsFullWidth = true, Validator = PlayerInputSettings.Validate };
        Input.CommitAsync = value => _inputSetting.TrySetValueAsync(value);
        _display = display ?? new AvaloniaWindowDisplayService();
        _audioPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _audioPreviewTimer.Tick += OnAudioPreviewTimerTick;
        _liveSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _liveSaveTimer.Tick += OnLiveSaveTimerTick;
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
        Input.ToggleFullscreenAsync = async () =>
        {
            if (_display.IsAvailable)
                await _displayMode.TrySetValueAsync(_display.Mode == PlayerDisplayMode.Window
                    ? PlayerDisplayMode.BorderlessFullscreen : PlayerDisplayMode.Window);
        };
        _categories = new System.Collections.ObjectModel.ObservableCollection<SettingCategory>(new[]
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
            new SettingCategory("operation", "Settings.Operation", [_inputSetting], localization.GetString("Settings.Operation")),
            new SettingCategory("graphics", "Settings.Graphics", [_displayMode, _windowSize], localization.GetString("Settings.Graphics")),
            new SettingCategory("audio", "Settings.Audio",
            [
                Number("masterVolume", "Settings.MasterVolume", 1, 1, .01, () => RuntimeSettings.MasterVolume, value => RuntimeSettings.MasterVolume = (float)value, NumericEditorKind.Slider, livePreview: true),
                Boolean("masterMuted", "Settings.MasterMuted", false, () => RuntimeSettings.MasterMuted, value => RuntimeSettings.MasterMuted = value)
            ], localization.GetString("Settings.Audio"))
        });
        _coordinator = new SettingsCoordinator { SaveAsync = token => _variables?.FlushPlayerVariablesAsync(token) ?? Task.CompletedTask };
        _coordinator.Attach(Categories);
        _baseAudioItems = Categories.Last().Items;
        _audioRestoreStart = _restoreValues.Count;
        localization.CultureService.CultureChanged += OnCultureChanged;
        _display.Changed += OnDisplayChanged;
    }

    public IReadOnlyList<SettingCategory> Categories => _categories;
    public EditorRegistry Editors { get; }
    public Task FlushAsync() => AvaloniaUiDispatcher.Instance.InvokeAsync(FlushOnUiAsync);

    private async Task FlushOnUiAsync()
    {
        FlushAudioPreview();
        _liveSaveTimer.Stop();
        if (_liveSavePending)
        {
            var revision = _liveSaveRevision;
            await _coordinator.RequestSaveAsync();
            if (revision == _liveSaveRevision) _liveSavePending = false;
        }
        await _coordinator.FlushAsync();
    }

    public void BindWindow(global::Avalonia.Controls.Window? window)
    {
        _gamepadInput.Bind(window);
        if (_display is AvaloniaWindowDisplayService desktop) desktop.Bind(window);
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

    public async Task AttachAsync(PlayerVariableService variables, GameSettings runtimeSettings,
        GalNet.Core.Audio.AudioConfiguration? audioConfiguration = null, bool showTrackControls = false,
        CancellationToken cancellationToken = default)
    {
        await FlushAsync();
        _variables = variables;
        _preferences = new PlayerPreferences(variables);
        RuntimeSettings = runtimeSettings;
        _restoring = true;
        try
        {
            ConfigureAudio(audioConfiguration ?? GalNet.Core.Audio.AudioConfiguration.Empty, showTrackControls);
            var stored = variables.GetSnapshot(VariableScope.Player);
            Input.Restore(stored.TryGetValue("player.galnet.input.bindings", out var bindings) && bindings.Type == VariableType.String ? bindings.AsString() : null);
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
        Func<double> read, Action<double> write, NumericEditorKind editor = NumericEditorKind.NumericUpDown, bool livePreview = false)
    {
        var variable = "player.galnet.settings." + id;
        _restoreValues.Add(_ => write(_preferences!.Number(variable, defaultValue, maximum)));
        return new DoubleSetting(id, key, defaultValue, 0, maximum, step, read,
            (value, token) => ApplyRuntimeSettingAsync(variable, () => write(value), (float)value, token), _localization.GetString(key))
        {
            EditorKind = editor,
            LivePreview = livePreview ? value => PreviewRuntimeSetting(variable, () => write(value), (float)value) : null
        };
    }

    private void PreviewRuntimeSetting(string variable, Action apply, object storedValue)
    {
        apply();
        if (!_audioPreviewPending)
        {
            _audioPreviewPending = true;
            _audioPreviewTimer.Start();
        }
        Store(variable, storedValue);
        if (_restoring || _variables is null) return;
        _liveSavePending = true;
        _liveSaveRevision++;
        _liveSaveTimer.Stop();
        _liveSaveTimer.Start();
    }

    private void OnAudioPreviewTimerTick(object? sender, EventArgs args) => FlushAudioPreview();

    private void FlushAudioPreview()
    {
        _audioPreviewTimer.Stop();
        if (!_audioPreviewPending) return;
        _audioPreviewPending = false;
        try { SettingsChanged?.Invoke(RuntimeSettings); }
        catch (Exception exception) { GameLog.Logger.Error(exception, "Live audio settings preview failed"); }
    }

    private void OnLiveSaveTimerTick(object? sender, EventArgs args)
    {
        _liveSaveTimer.Stop();
        _ = PersistLivePreviewAsync();
    }

    private async Task PersistLivePreviewAsync()
    {
        if (!_liveSavePending) return;
        var revision = _liveSaveRevision;
        try
        {
            await _coordinator.RequestSaveAsync();
            if (revision == _liveSaveRevision) _liveSavePending = false;
        }
        catch (Exception exception) { GameLog.Logger.Error(exception, "Debounced player settings save failed"); }
    }

    private IntSetting Milliseconds(string id, string key, int defaultValue, int maximum, int step, Func<int> read, Action<int> write)
    {
        var variable = "player.galnet.settings." + id;
        _restoreValues.Add(_ => write(_preferences!.Integer(variable, defaultValue, maximum)));
        return new IntSetting(id, key, defaultValue, 0, maximum, step, read,
            (value, token) => ApplyRuntimeSettingAsync(variable, () => write(value), value, token), _localization.GetString(key))
        { EditorKind = NumericEditorKind.NumericUpDown, Format = "0" };
    }

    private BoolSetting Boolean(string id, string key, bool defaultValue, Func<bool> read, Action<bool> write)
    {
        var variable = "player.galnet.settings." + id;
        _restoreValues.Add(_ => write(_preferences!.Boolean(variable, defaultValue)));
        return new BoolSetting(id, key, defaultValue, read,
            (value, token) => ApplyRuntimeSettingAsync(variable, () => write(value), value, token), _localization.GetString(key));
    }

    private async Task ApplyRuntimeSettingAsync(string variable, Action apply, object storedValue, CancellationToken cancellationToken)
    {
        apply();
        SettingsChanged?.Invoke(RuntimeSettings);
        await _gameplay.UpdatePlaybackOptionsAsync(RuntimeSettings.ToPlaybackOptions(), cancellationToken);
        Store(variable, storedValue);
    }

    private void OnCultureChanged(object? sender, CultureChangedEventArgs args) => _language.Refresh();

    private SettingChoice<string>[] Languages() => _localization.CultureService.AvailableCultures
        .OrderBy(culture => culture.Name, StringComparer.Ordinal)
        .Select(culture => new SettingChoice<string>(culture.Name, culture.Name, culture.Name, culture.NativeName)).ToArray();

    private void Store(string name, object value)
    {
        if (_restoring) return;
        _preferences?.Set(name, value);
    }

    public void Dispose()
    {
        _gamepadInput.Dispose();
        Input.Dispose();
        _audioPreviewTimer.Stop();
        _audioPreviewTimer.Tick -= OnAudioPreviewTimerTick;
        _liveSaveTimer.Stop();
        _liveSaveTimer.Tick -= OnLiveSaveTimerTick;
        _localization.CultureService.CultureChanged -= OnCultureChanged;
        _display.Changed -= OnDisplayChanged;
        if (_display is AvaloniaWindowDisplayService desktop) desktop.Dispose();
    }
}
