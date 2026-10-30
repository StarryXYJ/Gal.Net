using System.Buffers;
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
            await compressor.WriteAsync(data, ct);
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

    /// <summary>Decompresses exactly the declared length, leaving the source open.</summary>
    public static byte[] Decompress(Stream source, Core.Assets.CompressionMode mode, int expectedLength, CancellationToken ct = default)
    {
        ValidateInput(source, mode, expectedLength, ct);
        using var decompressor = mode == Core.Assets.CompressionMode.None ? null : CreateDecompressor(source, mode);
        var input = decompressor ?? source;
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int count;
            do
            {
                ct.ThrowIfCancellationRequested();
                count = input.Read(buffer, 0, ReadSize(output.Length, expectedLength, buffer.Length));
                Append(output, buffer.AsSpan(0, count), expectedLength, ct);
            } while (count != 0);
            return Finish(output, expectedLength);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Asynchronously decompresses exactly the declared length, leaving the source open.</summary>
    public static async Task<byte[]> DecompressAsync(Stream source, Core.Assets.CompressionMode mode, int expectedLength, CancellationToken ct = default)
    {
        ValidateInput(source, mode, expectedLength, ct);
        using var decompressor = mode == Core.Assets.CompressionMode.None ? null : CreateDecompressor(source, mode);
        var input = decompressor ?? source;
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int count;
            do
            {
                ct.ThrowIfCancellationRequested();
                count = await input.ReadAsync(buffer.AsMemory(0, ReadSize(output.Length, expectedLength, buffer.Length)), ct).ConfigureAwait(false);
                Append(output, buffer.AsSpan(0, count), expectedLength, ct);
            } while (count != 0);
            return Finish(output, expectedLength);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static void ValidateInput(Stream source, Core.Assets.CompressionMode mode, int expectedLength, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ct.ThrowIfCancellationRequested();
    }

    // Read at most one extra byte to detect overflow without expanding the output buffer.
    private static int ReadSize(long written, int expectedLength, int bufferLength) =>
        (int)Math.Min(bufferLength, expectedLength - written + 1);

    private static void Append(MemoryStream output, ReadOnlySpan<byte> data, int expectedLength, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var length = output.Length + data.Length;
        if (length > expectedLength) throw new InvalidDataException("Decompressed data exceeds its declared length.");
        if (length > output.Capacity)
            output.Capacity = (int)Math.Min(expectedLength, Math.Max(length, (long)output.Capacity * 2));
        output.Write(data);
    }

    private static byte[] Finish(MemoryStream output, int expectedLength)
    {
        if (output.Length != expectedLength) throw new InvalidDataException("Decompressed data is shorter than its declared length.");
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
