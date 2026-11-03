using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Gallery;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using Serilog;
namespace GalNet.Sample.CrossPlatform.ViewModels;

public abstract partial class GalleryGridPageViewModel(IGameNavigationService navigation, GalleryMediaService media)
    : BackNavigablePageViewModel(navigation), IGalleryPageViewModel, IDisposable, IAsyncDisposable
{
    private readonly List<Task> _pendingLoads = [];
    private GalleryEntryViewModel[] _visibleRows = [];
    private bool _preparing;
    private bool _disposed;
    protected GalleryMediaService Media => media;
    public GalleryThumbnailCollection Thumbnails { get; } = new(media);
    private PageSlice<GalleryEntryViewModel>? _pages;
    public PageSlice<GalleryEntryViewModel> Pages => _pages ??= CreatePages();
    private PageSlice<GalleryEntryViewModel> CreatePages()
    {
        var pages = new PageSlice<GalleryEntryViewModel>(Thumbnails.Items, SampleCustomization.PageSize);
        pages.PropertyChanged += OnPageChanged;
        return pages;
    }
    public Task ActivateAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _preparing = true;
        try { Pages.UpdateSource(() => Thumbnails.Prepare(gallery)); }
        finally { _preparing = false; }
        LoadCurrentPage(cancellationToken);
        return Task.CompletedTask;
    }
    protected abstract Task<Bitmap?> LoadThumbnailAsync(GalleryItemData item, CancellationToken cancellationToken);
    protected abstract Task OpenDetailAsync(GalleryItemData item, CancellationToken cancellationToken);
    [RelayCommand] private Task OpenItemAsync(GalleryEntryViewModel item, CancellationToken cancellationToken) => item.IsUnlocked ? OpenDetailAsync(item.Data, cancellationToken) : Task.CompletedTask;
    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_preparing && e.PropertyName == nameof(PageSlice<GalleryEntryViewModel>.CurrentPage)) LoadCurrentPage(CancellationToken.None);
    }
    private void LoadCurrentPage(CancellationToken cancellationToken)
    {
        Thumbnails.CancelLoading();
        foreach (var row in _visibleRows) { row.Thumbnail?.Dispose(); row.Thumbnail = null; }
        _visibleRows = Pages.Items.ToArray();
        _pendingLoads.RemoveAll(task => task.IsCompleted);
        _pendingLoads.Add(ObserveAsync(Thumbnails.LoadAsync(_visibleRows, LoadThumbnailAsync, cancellationToken)));
    }
    private static async Task ObserveAsync(Task load)
    {
        try { await load; }
        catch (Exception error) { Log.Error(error, "Gallery thumbnail loading failed"); }
    }
    protected override void OnBack()
    {
        Thumbnails.CancelLoading();
        foreach (var row in _visibleRows) { row.Thumbnail?.Dispose(); row.Thumbnail = null; }
        _visibleRows = [];
        base.OnBack();
    }
    private void StopLoading()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pages is not null) { _pages.PropertyChanged -= OnPageChanged; _pages.Dispose(); }
        Thumbnails.Dispose();
        _visibleRows = [];
    }
    public void Dispose() { StopLoading(); GC.SuppressFinalize(this); }
    public async ValueTask DisposeAsync()
    {
        StopLoading();
        await Task.WhenAll(_pendingLoads);
        GC.SuppressFinalize(this);
    }
}
