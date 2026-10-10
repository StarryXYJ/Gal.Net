using DynamicLocalization.Core;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Owns the official demo's observable LocalizedString values for the Sample lifetime.</summary>
public sealed class SampleLocalizationService(CultureService cultureService) : IDisposable
{
    private readonly Dictionary<string, LocalizedString> _strings = new(StringComparer.Ordinal);
    public static SampleLocalizationService? Current { get; private set; }

    public CultureService CultureService { get; } = cultureService;

    public string this[string key] => CultureService[key];

    public void ApplyCulture(string cultureName)
    {
        CultureService.SetCulture(cultureName);
    }

    public LocalizedString GetString(string key)
    {
        if (!_strings.TryGetValue(key, out var text))
            _strings.Add(key, text = new LocalizedString(CultureService, key));
        return text;
    }

    public void Dispose()
    {
        foreach (var text in _strings.Values) text.Dispose();
        _strings.Clear();
        if (ReferenceEquals(Current, this))
        {
            Current = null;
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = null;
        }
    }

    public static SampleLocalizationService Create(CultureService cultureService)
    {
        var service = new SampleLocalizationService(cultureService);
        Current = service;
        return service;
    }
}
