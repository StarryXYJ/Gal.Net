using System.Globalization;
using DynamicLocalization.Core.Providers;
using GalNet.Core.Services;
using GalNet.Runtime.Content;

namespace GalNet.Sample.Localization;

/// <summary>Ordered, immutable JSON sources with explicit game-language fallback.</summary>
public sealed class GameTextResolver : ITextResolver, ILocalizationProvider
{
    private readonly List<(CultureInfo Culture, JsonLocalizationProvider Provider)> _sources = [];
    private readonly CultureInfo _defaultCulture;
    private readonly Func<CultureInfo> _currentCulture;

    public GameTextResolver(GameContent content, Func<CultureInfo> currentCulture)
    {
        _defaultCulture = content.Settings.TargetLocale.ToCultureInfo();
        _currentCulture = currentCulture;
        foreach (var source in content.Localization.Sources)
        {
            // LoadJsonString replaces a culture's dictionary, so each file needs its own provider.
            var provider = new JsonLocalizationProvider();
            provider.LoadJsonString(source.Json, source.Culture);
            _sources.Add((CultureInfo.GetCultureInfo(source.Culture), provider));
        }
    }

    public string Name => "Sample.Game";
    public IEnumerable<CultureInfo> GetAvailableCultures() => _sources.Select(source => source.Culture).Distinct();
    public string Resolve(string key) => GetString(key, _currentCulture());
    public string GetString(string key, CultureInfo culture) => TryGetString(key, culture, out var value) ? value! : key;

    public bool TryGetString(string key, CultureInfo culture, out string? value) =>
        TryCultureChain(key, culture, out value) || TryCultureChain(key, _defaultCulture, out value);

    private bool TryCultureChain(string key, CultureInfo culture, out string? value)
    {
        for (var candidate = culture; candidate.Name.Length > 0; candidate = candidate.Parent)
            for (var index = _sources.Count - 1; index >= 0; index--)
            {
                var source = _sources[index];
                if (source.Culture.Equals(candidate) && source.Provider.TryGetString(key, candidate, out value) && value is not null)
                    return true;
            }
        value = null;
        return false;
    }

    // Sources are snapshots of loaded content. A new content session supplies a new resolver.
    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
