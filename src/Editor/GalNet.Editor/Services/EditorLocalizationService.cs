using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Views;

namespace GalNet.Editor.Services;

public sealed class EditorLocalizationService : IEditorLocalizationService
{
    private readonly CultureService _cultureService;
    private readonly Dictionary<string, string> _fallback;
    public const string DefaultLocale = "zh-CN";

    public static IEditorLocalizationService? Current { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<CultureInfo> AvailableCultures { get; } =
    [
        new("zh-CN"),
        new("en-US"),
        new("ja-JP"),
        new("ko-KR")
    ];

    public CultureInfo CurrentCulture => _cultureService.CurrentCulture;

    public string this[string key]
    {
        get
        {
            var value = _cultureService[key];
            if (!string.IsNullOrEmpty(value) && value != key && value != "#" + key + "#") return value;
            return _fallback.GetValueOrDefault(key, "[" + key + "]");
        }
    }

    public EditorLocalizationService() : this(Resources.Load()) { }

    internal EditorLocalizationService(Resources resources)
    {
        _cultureService = new CultureService();
        Current = this;

        _cultureService.RegisterProvider(resources.Provider);
        _fallback = resources.Fallback;
        ApplyLocale(DefaultLocale);
    }

    internal sealed record Resources(JsonLocalizationProvider Provider, Dictionary<string, string> Fallback)
    {
        internal static Resources Load()
        {
            var assembly = typeof(MainWindow).Assembly;
            var provider = new JsonLocalizationProvider();
            provider.Initialize(new JsonLocalizationProviderOptions
            {
                BasePath = "Localization",
                UseEmbeddedResources = true,
                Assembly = assembly
            });
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("Localization.zh-CN.json", StringComparison.Ordinal)))!;
            return new(provider, JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
        }
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        return string.Format(CurrentCulture, template, args);
    }

    public void ApplyLocale(string localeCode)
    {
        localeCode = string.IsNullOrWhiteSpace(localeCode) ? DefaultLocale : localeCode.Replace("_", "-", StringComparison.Ordinal);
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(localeCode); }
        catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo(DefaultLocale); }
        ApplyCulture(culture);
    }

    public void ApplyCulture(CultureInfo culture)
    {
        culture = AvailableCultures.FirstOrDefault(item => item.Name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase))
            ?? AvailableCultures.FirstOrDefault(item => item.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
            ?? CultureInfo.GetCultureInfo(DefaultLocale);
        if (_cultureService.CurrentCulture.Name == culture.Name) return;

        _cultureService.SetCulture(culture.Name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
