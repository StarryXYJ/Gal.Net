using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BenchmarkDotNet.Attributes;

namespace GalNet.Sample.Benchmarks;

/// <summary>Compare full and bounded decode of one generated, deterministic PNG.</summary>
[MemoryDiagnoser]
public class ThumbnailBenchmarks : IDisposable
{
    private string _file = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GalNet.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var directory = Path.Combine(root.FullName, "output", "sample-gallery-fixtures");
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, Guid.NewGuid() + ".png");
        using var image = new WriteableBitmap(new PixelSize(2560, 1440), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var pixels = image.Lock())
        {
            var bytes = new byte[pixels.RowBytes * pixels.Size.Height];
            new Random(42).NextBytes(bytes);
            Marshal.Copy(bytes, 0, pixels.Address, bytes.Length);
        }
        image.Save(_file);
    }

    [Benchmark(Baseline = true)]
    public PixelSize DecodeOriginal()
    {
        using var bitmap = new Bitmap(_file);
        return bitmap.PixelSize;
    }

    [Benchmark]
    public PixelSize DecodeAt768()
    {
        using var stream = File.OpenRead(_file);
        using var bitmap = Bitmap.DecodeToWidth(stream, 768);
        return bitmap.PixelSize;
    }

    public void Verify()
    {
        if (DecodeOriginal() != new PixelSize(2560, 1440) || DecodeAt768() != new PixelSize(768, 432))
            throw new InvalidDataException("Thumbnail fixture or decoded size is invalid.");
    }

    [GlobalCleanup]
    public void Dispose()
    {
        if (_file is not null && File.Exists(_file)) File.Delete(_file);
        GC.SuppressFinalize(this);
    }
}
