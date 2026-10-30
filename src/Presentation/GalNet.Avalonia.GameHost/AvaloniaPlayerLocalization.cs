using System.Globalization;
using System.Reflection;
using DynamicLocalization.Core;
using DynamicLocalization.Core.Providers;
using GalNet.Core.Services;
using GalNet.GameHost.Content;
using GalNet.Runtime.I18n;

namespace GalNet.Avalonia.GameHost;

/// <summary>Owns observable localized strings and the replaceable game text provider.</summary>
public sealed class AvaloniaPlayerLocalization(CultureService cultureService,
    IEnumerable<ILocalizationProvider>? bootstrapProviders = null) : IDisposable, IPlayerLocalization
{
    private readonly Dictionary<string, LocalizedString> _strings = new(StringComparer.Ordinal);
    private TextProvider? _gameProvider;
    private readonly ILocalizationProvider[] _bootstrapProviders = bootstrapProviders?.ToArray() ?? [];
    private LocalizedTextSource[]? BootstrapSources { get; init; }

    public CultureService CultureService { get; } = cultureService;

    public string this[string key] => CultureService[key];

    /// <summary>Loads common player UI text followed by project-owned resource assemblies.</summary>
    public static AvaloniaPlayerLocalization FromEmbeddedResources(IEnumerable<Assembly> resourceAssemblies,
        string defaultCulture = "en-US")
    {
        var cultureService = new CultureService();
        var sources = resourceAssemblies.Prepend(typeof(AvaloniaPlayerLocalization).Assembly).Distinct()
            .SelectMany(assembly =>
            {
                var provider = new JsonLocalizationProvider();
                provider.Initialize(new JsonLocalizationProviderOptions
                {
                    BasePath = "Localization",
                    UseEmbeddedResources = true,
                    Assembly = assembly
                });
                return provider.GetAvailableCultures().Select(culture => new LocalizedTextSource(
                    culture, key => provider.TryGetString(key, culture, out var value) ? value : null));
            }).ToArray();
        // One provider preserves ordered overrides even when JSON providers share a Name.
        var bootstrap = new TextProvider(new GameTextResolver(sources, CultureInfo.GetCultureInfo(defaultCulture),
            () => cultureService.CurrentCulture), "GalNet.UI");
        cultureService.RegisterProvider(bootstrap);
        cultureService.SetCulture(defaultCulture);
        return new(cultureService, [bootstrap]) { BootstrapSources = sources };
    }

    public void AttachGame(GameContent content, IEnumerable<GameLocalizationSource>? additionalSources = null)
    {
        // Content overrides must see exact source cultures before default-culture fallback.
        var sources = BootstrapSources?.AsEnumerable() ?? _bootstrapProviders.SelectMany(provider => provider.GetAvailableCultures().Select(culture =>
            new LocalizedTextSource(culture, key => provider.TryGetString(key, culture, out var value) ? value : null)));
        var provider = new TextProvider(content.Localization.CreateResolver(
            () => CultureService.CurrentCulture, sources, additionalSources));
        foreach (var bootstrap in _bootstrapProviders) CultureService.UnregisterProvider(bootstrap.Name);
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
        if (GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve?.Target == this)
            GalNet.Avalonia.GameView.MarkupExtensions.PlayerTextExtension.Resolve = null;
    }

    private sealed class TextProvider(GameTextResolver resolver, string name = "GalNet.Game") : ILocalizationProvider
    {
        public string Name => name;
        public IEnumerable<CultureInfo> GetAvailableCultures() => resolver.AvailableCultures;
        public string GetString(string key, CultureInfo culture) => resolver.GetString(key, culture);
        public bool TryGetString(string key, CultureInfo culture, out string? value) => resolver.TryGetString(key, culture, out value);
        public string Resolve(string key) => resolver.Resolve(key);
        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
