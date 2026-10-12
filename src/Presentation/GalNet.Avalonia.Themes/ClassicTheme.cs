using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Layouts.Classic;

namespace GalNet.Avalonia.Themes;

public sealed class ClassicTheme(GamePalette? palette = null) : GameLayoutTheme(palette ?? GamePalette.Ocean)
{
    protected override string LayoutName => "Classic";
    public override void RegisterViews(IPageViewRegistryBuilder views)
    {
        views.Register<ScreenshotDialogViewModel, ClassicScreenshotDialog>();
        views.Register<TitlePageViewModel, ClassicTitlePage>();
        views.Register<SettingsPageViewModel, ClassicSettingsPage>();
        views.Register<GalleryPageViewModel, ClassicGalleryPage>();
        views.Register<CgGalleryPageViewModel, ClassicCgGalleryPage>();
        views.Register<CgGalleryImagePageViewModel, ClassicCgGalleryImagePage>();
        views.Register<AudioGalleryPageViewModel, ClassicAudioGalleryPage>();
        views.Register<VideoGalleryPageViewModel, ClassicVideoGalleryPage>();
        views.Register<VideoGalleryPlayerPageViewModel, ClassicVideoGalleryPlayerPage>();
        views.Register<AboutPageViewModel, ClassicAboutPage>();
        views.Register<SaveSlotsPageViewModel, ClassicSaveSlotsPage>();
        views.Register<MissingGalleryPageViewModel, ClassicMissingGalleryPage>();
        views.Register<LoadingPageViewModel, ClassicLoadingPage>();
    }
}
