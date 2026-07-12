using System.ComponentModel;
using System.Globalization;
using System.Collections.Generic;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Views;

namespace GalNet.Editor.Services;

public sealed class EditorLocalizationService : IEditorLocalizationService
{
    private readonly CultureService _cultureService;

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

    public string this[string key] => _cultureService[key];

    public EditorLocalizationService()
    {
        _cultureService = new CultureService();
        Current = this;

        var provider = new JsonLocalizationProvider();
        provider.Initialize(new JsonLocalizationProviderOptions
        {
            BasePath = "Localization",
            UseEmbeddedResources = true,
            Assembly = typeof(MainWindow).Assembly
        });

        _cultureService.RegisterProvider(provider);
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        return string.Format(CultureInfo.CurrentUICulture, template, args);
    }

    public void ApplyLocale(string localeCode)
    {
        ApplyCulture(new CultureInfo(localeCode));
    }

    public void ApplyCulture(CultureInfo culture)
    {
        if (_cultureService.CurrentCulture.Name == culture.Name) return;

        _cultureService.SetCulture(culture.Name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
