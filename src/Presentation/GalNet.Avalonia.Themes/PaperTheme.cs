using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Layouts.Paper;

namespace GalNet.Avalonia.Themes;

public sealed class PaperTheme(GamePalette? palette = null) : GameLayoutTheme(palette ?? GamePalette.Rose)
{
    protected override string LayoutName => "Paper";
    public override void RegisterViews(IPageViewRegistryBuilder views) => views.Register<TitlePageViewModel, PaperTitlePage>();
}
