using System.IO.Compression;
using GalNet.Core.Assets;
using IOCompressionMode = System.IO.Compression.CompressionMode;

namespace GalNet.Assets;

/// <summary>
/// 压缩/解压工具类。
/// 使用 .NET 内置的 System.IO.Compression。
/// </summary>
public static class CompressionHelper
{
    /// <summary>压缩数据。</summary>
    public static byte[] Compress(byte[] data, Core.Assets.CompressionMode mode)
    {
        if (mode == Core.Assets.CompressionMode.None || data.Length == 0)
            return data;

        using var output = new MemoryStream();
        using (var compressor = CreateCompressor(output, mode))
        {
            compressor.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /// <summary>异步压缩数据。</summary>
    public static async Task<byte[]> CompressAsync(byte[] data, Core.Assets.CompressionMode mode, CancellationToken ct = default)
    {
        if (mode == Core.Assets.CompressionMode.None || data.Length == 0)
            return data;

        using var output = new MemoryStream();
        using (var compressor = CreateCompressor(output, mode))
        {
            await compressor.WriteAsync(data, 0, data.Length, ct);
        }
        return output.ToArray();
    }

    /// <summary>解压数据。</summary>
    public static byte[] Decompress(byte[] compressed, Core.Assets.CompressionMode mode)
    {
        if (mode == Core.Assets.CompressionMode.None || compressed.Length == 0)
            return compressed;

        using var input = new MemoryStream(compressed);
        using var decompressor = CreateDecompressor(input, mode);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>异步解压数据。</summary>
    public static async Task<byte[]> DecompressAsync(byte[] compressed, Core.Assets.CompressionMode mode, CancellationToken ct = default)
    {
        if (mode == Core.Assets.CompressionMode.None || compressed.Length == 0)
            return compressed;

        using var input = new MemoryStream(compressed);
        using var decompressor = CreateDecompressor(input, mode);
        using var output = new MemoryStream();
        await decompressor.CopyToAsync(output, ct);
        return output.ToArray();
    }

    /// <summary>流式压缩（写入时实时压缩）。</summary>
    public static Stream CreateCompressStream(Stream destination, Core.Assets.CompressionMode mode)
    {
        return mode switch
        {
            Core.Assets.CompressionMode.Deflate => new DeflateStream(destination, CompressionLevel.Optimal, leaveOpen: true),
            Core.Assets.CompressionMode.GZip => new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true),
            Core.Assets.CompressionMode.Brotli => new BrotliStream(destination, CompressionLevel.Optimal, leaveOpen: true),
            _ => destination,
        };
    }

    /// <summary>流式解压（读取时实时解压）。</summary>
    public static Stream CreateDecompressStream(Stream source, Core.Assets.CompressionMode mode)
    {
        return mode switch
        {
            Core.Assets.CompressionMode.Deflate => new DeflateStream(source, IOCompressionMode.Decompress, leaveOpen: true),
            Core.Assets.CompressionMode.GZip => new GZipStream(source, IOCompressionMode.Decompress, leaveOpen: true),
            Core.Assets.CompressionMode.Brotli => new BrotliStream(source, IOCompressionMode.Decompress, leaveOpen: true),
            _ => source,
        };
    }

    private static Stream CreateCompressor(Stream destination, Core.Assets.CompressionMode mode)
    {
        return mode switch
        {
            Core.Assets.CompressionMode.Deflate => new DeflateStream(destination, CompressionLevel.Optimal, leaveOpen: true),
            Core.Assets.CompressionMode.GZip => new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true),
            Core.Assets.CompressionMode.Brotli => new BrotliStream(destination, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private static Stream CreateDecompressor(Stream source, Core.Assets.CompressionMode mode)
    {
        return mode switch
        {
            Core.Assets.CompressionMode.Deflate => new DeflateStream(source, IOCompressionMode.Decompress, leaveOpen: true),
            Core.Assets.CompressionMode.GZip => new GZipStream(source, IOCompressionMode.Decompress, leaveOpen: true),
            Core.Assets.CompressionMode.Brotli => new BrotliStream(source, IOCompressionMode.Decompress, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
