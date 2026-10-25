using System.Globalization;
using GalNet.Core.Services;

namespace GalNet.Runtime.I18n;

/// <summary>A culture-specific lookup supplied by the host's localization adapter.</summary>
public sealed record LocalizedTextSource(CultureInfo Culture, Func<string, string?> Lookup);

/// <summary>Culture-first lookup with ordered overrides, parent cultures and game-default fallback.</summary>
public sealed class GameTextResolver : ITextResolver
{
    private readonly LocalizedTextSource[] _sources;
    private readonly CultureInfo _defaultCulture;
    private readonly Func<CultureInfo> _currentCulture;

    public GameTextResolver(IEnumerable<LocalizedTextSource> sources, CultureInfo defaultCulture, Func<CultureInfo> currentCulture)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(defaultCulture);
        ArgumentNullException.ThrowIfNull(currentCulture);
        _sources = sources.ToArray();
        _defaultCulture = defaultCulture;
        _currentCulture = currentCulture;
        AvailableCultures = Array.AsReadOnly(_sources.Select(source => source.Culture).Distinct().ToArray());
    }

    public IReadOnlyList<CultureInfo> AvailableCultures { get; }
    public string Resolve(string key) => GetString(key, _currentCulture());
    public string GetString(string key, CultureInfo culture) => TryGetString(key, culture, out var value) ? value! : key;

    public bool TryGetString(string key, CultureInfo culture, out string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = "";
            return true;
        }
        return TryCultureChain(key, culture, out value) || TryCultureChain(key, _defaultCulture, out value);
    }

    private bool TryCultureChain(string key, CultureInfo culture, out string? value)
    {
        for (var candidate = culture; candidate.Name.Length > 0; candidate = candidate.Parent)
            for (var index = _sources.Length - 1; index >= 0; index--)
            {
                var source = _sources[index];
                if (source.Culture.Equals(candidate) && source.Lookup(key) is { } found)
                {
                    value = found;
                    return true;
                }
            }
        value = null;
        return false;
    }
}
