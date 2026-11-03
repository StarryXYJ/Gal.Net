using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class CompressionHelperTests
{
    private static readonly byte[] SampleData = "Hello GalNet! 你好 GalNet! こんにちは GalNet!"u8.ToArray();

    [Test]
    public void Compress_Deflate_Decompress_ReturnsOriginal()
    {
        var compressed = CompressionHelper.Compress(SampleData, CompressionMode.Deflate);
        Assert.That(compressed.Length, Is.LessThan(SampleData.Length));

        var decompressed = CompressionHelper.Decompress(new MemoryStream(compressed), CompressionMode.Deflate, SampleData.Length);
        Assert.That(decompressed, Is.EqualTo(SampleData));
    }

    [Test]
    public void Compress_GZip_Decompress_ReturnsOriginal()
    {
        var compressed = CompressionHelper.Compress(SampleData, CompressionMode.GZip);
        var decompressed = CompressionHelper.Decompress(new MemoryStream(compressed), CompressionMode.GZip, SampleData.Length);
        Assert.That(decompressed, Is.EqualTo(SampleData));
    }

    [Test]
    public void Compress_Brotli_Decompress_ReturnsOriginal()
    {
        var compressed = CompressionHelper.Compress(SampleData, CompressionMode.Brotli);
        var decompressed = CompressionHelper.Decompress(new MemoryStream(compressed), CompressionMode.Brotli, SampleData.Length);
        Assert.That(decompressed, Is.EqualTo(SampleData));
    }

    [Test]
    public void Compress_None_ReturnsSameData()
    {
        var compressed = CompressionHelper.Compress(SampleData, CompressionMode.None);
        Assert.That(compressed, Is.SameAs(SampleData));
    }

    [Test]
    public void Compress_EmptyData_ReturnsEmpty()
    {
        var compressed = CompressionHelper.Compress([], CompressionMode.Brotli);
        Assert.That(compressed, Is.Empty);
    }

    [Test]
    public void Decompress_EmptyData_ReturnsEmpty()
    {
        var decompressed = CompressionHelper.Decompress(new MemoryStream(), CompressionMode.Brotli, 0);
        Assert.That(decompressed, Is.Empty);
    }

    [Test]
    public void Compress_SmallData_Skipped([Values] CompressionMode mode)
    {
        if (mode == CompressionMode.None) return;

        // Data smaller than threshold should still compress
        var tiny = "ab"u8.ToArray();
        var compressed = CompressionHelper.Compress(tiny, mode);
        var decompressed = CompressionHelper.Decompress(new MemoryStream(compressed), mode, tiny.Length);
        Assert.That(decompressed, Is.EqualTo(tiny));
    }

    [Test]
    public async Task CompressAsync_DecompressAsync_ReturnsOriginal()
    {
        var compressed = await CompressionHelper.CompressAsync(SampleData, CompressionMode.Brotli);
        var decompressed = await CompressionHelper.DecompressAsync(new MemoryStream(compressed), CompressionMode.Brotli, SampleData.Length);
        Assert.That(decompressed, Is.EqualTo(SampleData));
    }

    [Test]
    public void CreateCompressStream_CreateDecompressStream_Roundtrip()
    {
        using var compressedStream = new MemoryStream();
        using (var compress = CompressionHelper.CreateCompressStream(compressedStream, CompressionMode.Deflate))
        {
            compress.Write(SampleData);
        }

        var compressed = compressedStream.ToArray();
        using var decompress = CompressionHelper.CreateDecompressStream(new MemoryStream(compressed), CompressionMode.Deflate);
        using var result = new MemoryStream();
        decompress.CopyTo(result);

        Assert.That(result.ToArray(), Is.EqualTo(SampleData));
    }
}
