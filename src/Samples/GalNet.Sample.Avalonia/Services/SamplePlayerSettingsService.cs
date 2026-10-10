using System.Collections.ObjectModel;
using DynamicLocalization.Core;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Variable;
using GalNet.Runtime.Logging;
using GalNet.Storage.FileSystem;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Applies preferences and persists them through the session's player variables.</summary>
public sealed class SamplePlayerSettingsService : IPlayerSettingsService, IDisposable
{
    public const string LocaleVariable = "player.galnet.settings.locale";
    public const string TextSpeedVariable = "player.galnet.settings.textSpeed";
    private readonly SampleLocalizationService _localization;
    private readonly GamePageViewModel _gameplay;
    private readonly ChoicePlayerSetting _language;
    private readonly ObservableCollection<SettingOption> _languages = [];
    private FileVariableService? _variables;
    private Task _pendingSave = Task.CompletedTask;
    private bool _restoring;

    public SamplePlayerSettingsService(SampleLocalizationService localization, GamePageViewModel gameplay)
    {
        _localization = localization;
        _gameplay = gameplay;
        RefreshLanguages();
        _language = new ChoicePlayerSetting("locale", localization.GetString("Settings.Language"),
            _languages,
            () => localization.CultureService.CurrentCulture.Name, localization.ApplyCulture);
        Items = [_language, new RangePlayerSetting("textSpeed", localization.GetString("Settings.TextSpeed"),
            0, 120, () => gameplay.TextSpeed, value =>
            {
                gameplay.TextSpeed = value;
                Save(TextSpeedVariable, value);
            })];
        localization.CultureService.CultureChanged += OnCultureChanged;
        localization.CultureService.ProvidersChanged += OnProvidersChanged;
    }

    public IReadOnlyList<PlayerSetting> Items { get; }
    public Task FlushAsync() => _pendingSave;

    public void Attach(FileVariableService variables)
    {
        _variables = variables;
        _restoring = true;
        try
        {
            var stored = variables.GetSnapshot(VariableScope.Player);
            if (stored.TryGetValue(LocaleVariable, out var locale)
                && _language.Options.Any(option => option.Value == locale.AsString()))
                _localization.ApplyCulture(locale.AsString());
            if (stored.TryGetValue(TextSpeedVariable, out var speed)
                && double.TryParse(speed.AsString(), System.Globalization.CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value))
                _gameplay.TextSpeed = Math.Clamp(value, 0, 120);
        }
        finally
        {
            _restoring = false;
            _language.Refresh();
            foreach (var setting in Items.OfType<RangePlayerSetting>()) setting.Refresh();
        }
    }

    private void OnCultureChanged(object? sender, CultureChangedEventArgs args)
    {
        _language.Refresh();
        Save(LocaleVariable, args.NewCulture.Name);
    }

    private void OnProvidersChanged(object? sender, EventArgs args)
    {
        RefreshLanguages();
        _language.Refresh();
    }

    private void RefreshLanguages()
    {
        _languages.Clear();
        foreach (var culture in _localization.CultureService.AvailableCultures.OrderBy(culture => culture.Name, StringComparer.Ordinal))
            _languages.Add(new(culture.Name, culture.NativeName));
    }

    private void Save(string name, object value)
    {
        if (_restoring || _variables is not { } variables) return;
        var variable = new GalNet.Core.Variable.Variable { Name = name };
        variable.SetValue(value);
        variables.NotifyVariableChanged(VariableScope.Player, name, variable);
        _pendingSave = PersistAsync(_pendingSave, variables);
    }

    private static async Task PersistAsync(Task previous, FileVariableService variables)
    {
        await previous;
        try { await variables.FlushPlayerVariablesAsync(); }
        catch (Exception error) { GameLog.Logger.Error(error, "Unable to save player settings"); }
    }

    public void Dispose()
    {
        _localization.CultureService.CultureChanged -= OnCultureChanged;
        _localization.CultureService.ProvidersChanged -= OnProvidersChanged;
    }
}
