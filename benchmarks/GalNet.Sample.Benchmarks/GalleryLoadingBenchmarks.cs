using Avalonia.Media.Imaging;
using BenchmarkDotNet.Attributes;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Contracts.Builtins.Gallery;

namespace GalNet.Sample.Benchmarks;

/// <summary>Measure the existing sequential activation with a controlled asynchronous loader.</summary>
public class GalleryLoadingBenchmarks : IDisposable
{
    private GalleryMediaService _media = null!;
    private GalleryThumbnailCollection _thumbnails = null!;
    private GalleryTypeData _gallery = null!;
    private int _completed;

    [Params(8, 64)] public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _media = new GalleryMediaService(null!);
        _thumbnails = new GalleryThumbnailCollection(_media);
        _gallery = new GalleryTypeData(new GalleryTypeRegistration { TypeId = "cg", ResourceTypeId = "image" },
            Enumerable.Range(0, ItemCount).Select(index => new GalleryItemData(new GalleryItem { Id = index }, true)).ToArray());
    }

    [Benchmark]
    public Task Activate() => _thumbnails.ActivateAsync(_gallery, LoadAsync, CancellationToken.None);

    private async Task<Bitmap?> LoadAsync(GalleryItemData item, CancellationToken cancellationToken)
    {
        await Task.Delay(5, cancellationToken);
        Interlocked.Increment(ref _completed);
        return null;
    }

    public async Task VerifyAsync()
    {
        await Activate();
        if (_thumbnails.Items.Count != ItemCount || _completed != ItemCount)
            throw new InvalidOperationException("Activation did not finish loading all of its unlocked items.");
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _thumbnails?.Dispose();
        _media?.Dispose();
        GC.SuppressFinalize(this);
    }
}
