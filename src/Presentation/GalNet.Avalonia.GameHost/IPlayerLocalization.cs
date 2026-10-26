using DynamicLocalization.Core;
using GalNet.Core.Services;
using GalNet.GameHost.Content;

namespace GalNet.Avalonia.GameHost;

/// <summary>Localization adapter used by the default Avalonia player services.</summary>
public interface IPlayerLocalization : ITextResolver
{
    CultureService CultureService { get; }
    LocalizedString GetString(string key);
    void ApplyCulture(string cultureName);
    void AttachGame(GameContent content, IEnumerable<GameLocalizationSource>? additionalSources = null);
}
