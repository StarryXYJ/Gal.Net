using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Layouts.Classic;

namespace GalNet.Avalonia.Themes;

public sealed class ClassicTheme(GamePalette? palette = null) : GameLayoutTheme(palette ?? GamePalette.Ocean)
{
    protected override string LayoutName => "Classic";
    public override void RegisterViews(IPageViewRegistryBuilder views) => views.Register<TitlePageViewModel, ClassicTitlePage>();
}
