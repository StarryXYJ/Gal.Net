using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using BenchmarkDotNet.Attributes;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Sample.CrossPlatform;
using GalNet.Sample.CrossPlatform.ViewModels;
using GalNet.Sample.CrossPlatform.Views;

namespace GalNet.Sample.Benchmarks;

/// <summary>Mount and measure the real Sample image gallery; media decoding is excluded.</summary>
[MemoryDiagnoser]
public class GalleryBenchmarks : IDisposable
{
    private GalleryMediaService _media = null!;
    private CgGalleryPageViewModel _model = null!;

    [Params(100, 1000)] public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        _media = new GalleryMediaService(null!);
        _model = new CgGalleryPageViewModel(null!, _media);
        for (var index = 0; index < ItemCount; index++)
            _model.Thumbnails.Items.Add(new GalleryEntryViewModel(new GalleryItemData(new GalleryItem { Id = index, ResourceId = $"image-{index}" }, true), $"Image {index}"));
    }

    [Benchmark]
    public void MountAndLayout()
    {
        var window = CreateWindow();
        try { window.Show(); window.UpdateLayout(); }
        finally { window.Close(); }
    }

    public int Verify()
    {
        var window = CreateWindow();
        try
        {
            window.Show(); window.UpdateLayout();
            var cards = window.GetVisualDescendants().OfType<Border>().Count(control => control.Name == "GalleryCard");
            if (_model.Thumbnails.Items.Count != ItemCount || cards < 1 || cards > ItemCount)
                throw new InvalidOperationException($"Expected {ItemCount} items and a nonempty card viewport, found {cards} cards.");
            return cards;
        }
        finally { window.Close(); }
    }

    private Window CreateWindow()
    {
        var view = new CgGalleryPage { DataContext = _model };
        var root = new Border { Child = view };
        root.Classes.Add("galnet-player");
        root.Styles.Add(SampleCustomization.CreateStyles());
        return new Window { Width = 1280, Height = 720, Content = root };
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _model?.Dispose();
        _media?.Dispose();
        GC.SuppressFinalize(this);
    }
}
