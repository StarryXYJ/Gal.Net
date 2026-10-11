using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes.Layouts.Minimal;

namespace GalNet.Avalonia.Themes;

public sealed class MinimalTheme(GamePalette? palette = null) : GameLayoutTheme(palette ?? GamePalette.Mono)
{
    protected override string LayoutName => "Minimal";
    public override void RegisterViews(IPageViewRegistryBuilder views)
    {
        views.Register<TitlePageViewModel, MinimalTitlePage>();
        views.Register<SettingsPageViewModel, MinimalSettingsPage>();
        views.Register<GalleryPageViewModel, MinimalGalleryPage>();
        views.Register<CgGalleryPageViewModel, MinimalCgGalleryPage>();
        views.Register<CgGalleryImagePageViewModel, MinimalCgGalleryImagePage>();
        views.Register<AudioGalleryPageViewModel, MinimalAudioGalleryPage>();
        views.Register<VideoGalleryPageViewModel, MinimalVideoGalleryPage>();
        views.Register<VideoGalleryPlayerPageViewModel, MinimalVideoGalleryPlayerPage>();
        views.Register<AboutPageViewModel, MinimalAboutPage>();
        views.Register<SaveSlotsPageViewModel, MinimalSaveSlotsPage>();
        views.Register<MissingGalleryPageViewModel, MinimalMissingGalleryPage>();
        views.Register<LoadingPageViewModel, MinimalLoadingPage>();
    }
}
