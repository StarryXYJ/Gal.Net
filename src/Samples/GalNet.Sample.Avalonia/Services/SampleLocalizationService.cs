using DynamicLocalization.Core;
using GalNet.Core.Services;
using GalNet.Runtime.Content;
using GalNet.Sample.Localization;

namespace GalNet.Sample.Avalonia.Services;

/// <summary>Owns the official demo's observable LocalizedString values for the Sample lifetime.</summary>
public sealed class SampleLocalizationService(CultureService cultureService) : IDisposable, ITextResolver
{
    private readonly Dictionary<string, LocalizedString> _strings = new(StringComparer.Ordinal);
    private GameTextResolver? _gameProvider;
    public static SampleLocalizationService? Current { get; private set; }

    public CultureService CultureService { get; } = cultureService;

    public string this[string key] => CultureService[key];

    public void AttachGame(GameContent content)
    {
        var provider = new GameTextResolver(content, () => CultureService.CurrentCulture);
        if (_gameProvider is not null) CultureService.UnregisterProvider(_gameProvider.Name);
        _gameProvider = provider;
        CultureService.RegisterProvider(provider);
    }

    public string Resolve(string key) => _gameProvider?.Resolve(key) ?? key;

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
        if (_gameProvider is not null) CultureService.UnregisterProvider(_gameProvider.Name);
        _gameProvider = null;
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
