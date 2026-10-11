using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Layouts.Paper;

namespace GalNet.Avalonia.Themes;

public sealed class PaperTheme(GamePalette? palette = null) : GameLayoutTheme(palette ?? GamePalette.Rose)
{
    protected override string LayoutName => "Paper";
    public override void RegisterViews(IPageViewRegistryBuilder views)
    {
        views.Register<TitlePageViewModel, PaperTitlePage>();
        views.Register<SettingsPageViewModel, PaperSettingsPage>();
        views.Register<GalleryPageViewModel, PaperGalleryPage>();
        views.Register<CgGalleryPageViewModel, PaperCgGalleryPage>();
        views.Register<CgGalleryImagePageViewModel, PaperCgGalleryImagePage>();
        views.Register<AudioGalleryPageViewModel, PaperAudioGalleryPage>();
        views.Register<VideoGalleryPageViewModel, PaperVideoGalleryPage>();
        views.Register<VideoGalleryPlayerPageViewModel, PaperVideoGalleryPlayerPage>();
        views.Register<AboutPageViewModel, PaperAboutPage>();
        views.Register<SaveSlotsPageViewModel, PaperSaveSlotsPage>();
        views.Register<MissingGalleryPageViewModel, PaperMissingGalleryPage>();
        views.Register<LoadingPageViewModel, PaperLoadingPage>();
    }
}
