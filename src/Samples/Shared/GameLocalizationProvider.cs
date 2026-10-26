using GalNet.Contracts.Builtins;
using System.Globalization;
using DynamicLocalization.Core.Providers;
using GalNet.Core.Services;
using GalNet.Runtime.Content;
using GalNet.Runtime.I18n;

namespace GalNet.Sample.Localization;

/// <summary>Adapts DynamicLocalization JSON providers to the Runtime-owned lookup policy.</summary>
public sealed class GameLocalizationProvider : ITextResolver, ILocalizationProvider
{
    private readonly GameTextResolver _resolver;

    public GameLocalizationProvider(GameContent content, Func<CultureInfo> currentCulture,
        IEnumerable<ILocalizationProvider>? bootstrapProviders = null,
        IEnumerable<GameLocalizationSource>? additionalSources = null)
    {
        var sources = new List<LocalizedTextSource>();
        foreach (var provider in bootstrapProviders ?? [])
            foreach (var culture in provider.GetAvailableCultures())
                sources.Add(Adapt(provider, culture));

        foreach (var source in content.Localization.Sources.Concat(content.Localization.ExternalSources).Concat(additionalSources ?? []))
        {
            // LoadJsonString replaces a culture's dictionary, so each file needs its own provider.
            var provider = new JsonLocalizationProvider();
            provider.LoadJsonString(source.Json, source.Culture);
            sources.Add(Adapt(provider, CultureInfo.GetCultureInfo(source.Culture)));
        }
        _resolver = new GameTextResolver(sources, content.Configuration.ReadDefaultOptions().TargetLocale.ToCultureInfo(), currentCulture);
    }

    public string Name => "Sample.Game";
    public IEnumerable<CultureInfo> GetAvailableCultures() => _resolver.AvailableCultures;
    public string Resolve(string key) => _resolver.Resolve(key);
    public string GetString(string key, CultureInfo culture) => _resolver.GetString(key, culture);
    public bool TryGetString(string key, CultureInfo culture, out string? value) => _resolver.TryGetString(key, culture, out value);

    private static LocalizedTextSource Adapt(ILocalizationProvider provider, CultureInfo culture) =>
        new(culture, key => provider.TryGetString(key, culture, out var value) ? value : null);

    // Sources are snapshots of loaded content. A new content session supplies a new resolver.
    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
