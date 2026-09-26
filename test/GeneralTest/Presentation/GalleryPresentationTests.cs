using System.Collections.ObjectModel;
using System.ComponentModel;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Gallery;

namespace GeneralTest.Presentation;

public sealed class GalleryPresentationTests
{
    [Test]
    public async Task TitleGalleryEntryIsHiddenWhenNoTypeHasContent()
    {
        var navigation = new RecordingNavigation();
        var session = new GallerySession([]);
        using var viewModel = new TitlePageViewModel(session, navigation, new GameLaunchFlow(navigation));

        await viewModel.OpenGalleryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasGallery, Is.False);
            Assert.That(navigation.TargetType, Is.Null);
        });
    }

    [Test]
    public async Task OneGalleryTypeNavigatesDirectlyToItsContent()
    {
        var navigation = new RecordingNavigation();
        var type = CreateType("cg", "sprite");
        var session = new GallerySession([type]);
        using var viewModel = new TitlePageViewModel(session, navigation, new GameLaunchFlow(navigation));

        await viewModel.OpenGalleryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.HasGallery, Is.True);
            Assert.That(navigation.TargetType, Is.EqualTo(typeof(GalleryContentPageViewModel)));
            Assert.That(navigation.Argument, Is.SameAs(type));
        });
    }

    [Test]
    public async Task MultipleGalleryTypesNavigateToTypeSelection()
    {
        var navigation = new RecordingNavigation();
        IReadOnlyList<GalleryTypeData> types = [CreateType("cg", "sprite"), CreateType("audio", "audio")];
        var session = new GallerySession(types);
        using var viewModel = new TitlePageViewModel(session, navigation, new GameLaunchFlow(navigation));

        await viewModel.OpenGalleryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(navigation.TargetType, Is.EqualTo(typeof(GalleryPageViewModel)));
            Assert.That(navigation.Argument, Is.SameAs(types));
        });
    }

    [TestCase("cg", "audio", GalleryRendererKind.Image)]
    [TestCase("custom-art", "sprite", GalleryRendererKind.Image)]
    [TestCase("custom-movie", "video", GalleryRendererKind.Video)]
    [TestCase("custom-music", "audio", GalleryRendererKind.Audio)]
    [TestCase("scene", "galgroup", GalleryRendererKind.Unsupported)]
    public void RendererUsesTypeOverrideThenResourceTypeFallback(
        string typeId,
        string resourceType,
        GalleryRendererKind expected)
    {
        Assert.That(GalleryRendererResolver.Resolve(typeId, resourceType), Is.EqualTo(expected));
    }

    private static GalleryTypeData CreateType(string typeId, string resourceType) => new(
        new GalleryTypeRegistration { TypeId = typeId, ResourceTypeName = resourceType },
        [new GalleryItemData(new GalleryItem { Id = $"{typeId}-item", TypeId = typeId, ResourceId = "resource" }, false)]);

    private sealed class GallerySession(IReadOnlyList<GalleryTypeData> types) : IGameSessionService, IGameGallerySession
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public string GameTitle => "Test";
        public string StatusMessage => "Ready";
        public bool IsReady => true;
        public bool IsPlaying => false;
        public bool CanContinue => false;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new([]);
        public IGalleryDataSource? GalleryDataSource { get; } = new StaticGalleryDataSource(types);
        public IGalleryResourceResolver? GalleryResources => null;
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StaticGalleryDataSource(IReadOnlyList<GalleryTypeData> types) : IGalleryDataSource
    {
        public IReadOnlyList<GalleryTypeData> GetTypes() => types;
    }

    private sealed class RecordingNavigation : IGameNavigationService
    {
        public Type? TargetType { get; private set; }
        public object? Argument { get; private set; }
        public PageViewModelBase? CurrentViewModel => null;
        public bool CanGoBack => false;
        public event EventHandler? CurrentViewModelChanged { add { } remove { } }
        public event EventHandler<GameNavigationChangedEventArgs>? Navigated { add { } remove { } }

        public void Navigate<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade)
            where TViewModel : PageViewModelBase => TargetType = typeof(TViewModel);

        public Task NavigateAsync<TViewModel, TArgs>(TArgs args, NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase, IActivatablePageViewModel<TArgs>
        {
            TargetType = typeof(TViewModel);
            Argument = args;
            return Task.CompletedTask;
        }

        public void ResetTo<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade)
            where TViewModel : PageViewModelBase => TargetType = typeof(TViewModel);

        public Task ResetToAsync<TViewModel>(NavigationTransition transition = NavigationTransition.CrossFade, CancellationToken cancellationToken = default)
            where TViewModel : PageViewModelBase
        {
            TargetType = typeof(TViewModel);
            return Task.CompletedTask;
        }

        public void GoBack(NavigationTransition transition = NavigationTransition.CrossFade) { }
    }
}
