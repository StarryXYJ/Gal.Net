using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Layouts.Minimal;

namespace GalNet.Avalonia.Themes;

public sealed class MinimalTheme(GamePalette? palette = null) : GameLayoutTheme(palette ?? GamePalette.Mono)
{
    protected override string LayoutName => "Minimal";
    public override void RegisterViews(IPageViewRegistryBuilder views) => views.Register<TitlePageViewModel, MinimalTitlePage>();
}
