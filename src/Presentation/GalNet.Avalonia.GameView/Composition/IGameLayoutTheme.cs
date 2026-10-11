using Avalonia.Styling;
using GalNet.Avalonia.GameView.Navigation;

namespace GalNet.Avalonia.GameView.Composition;

/// <summary>A startup-time layout choice. Use the same instance for styles and page registration.</summary>
public interface IGameLayoutTheme
{
    /// <summary>Creates a fresh set of player styles, including the configured palette.</summary>
    IStyle CreateStyles();

    /// <summary>Overrides default page views before the host applies its own overrides.</summary>
    void RegisterViews(IPageViewRegistryBuilder views);
}
