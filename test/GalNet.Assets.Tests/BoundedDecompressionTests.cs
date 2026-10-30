using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class BoundedDecompressionTests
{
    [Test]
    public async Task Exact_length_roundtrips_and_leaves_source_open(
        [Values] CompressionMode mode, [Values(0, 1, 256, 100_000)] int length, [Values] bool asynchronous)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        using var source = new MemoryStream(CompressionHelper.Compress(bytes, mode));
        Assert.That(await Read(source, mode, length, asynchronous), Is.EqualTo(bytes));
        Assert.That(source.CanRead, Is.True);
    }

    [Test]
    public void Incorrect_length_is_rejected(
        [Values] CompressionMode mode, [Values(-1, 1)] int difference, [Values] bool asynchronous)
    {
        var bytes = new byte[100_000];
        using var source = new MemoryStream(CompressionHelper.Compress(bytes, mode));
        Assert.ThrowsAsync<InvalidDataException>(() => Read(source, mode, bytes.Length + difference, asynchronous));
        Assert.That(source.CanRead, Is.True);
    }

    [Test]
    public void Large_declaration_does_not_preallocate_its_length([Values] CompressionMode mode, [Values] bool asynchronous)
    {
        using var source = new MemoryStream(CompressionHelper.Compress("small"u8.ToArray(), mode));
        Assert.ThrowsAsync<InvalidDataException>(() => Read(source, mode, int.MaxValue, asynchronous));
    }

    [Test]
    public void Overflow_reads_only_one_byte_beyond_the_limit([Values] bool asynchronous)
    {
        using var source = new ObservedStream(new byte[100_000]);
        Assert.ThrowsAsync<InvalidDataException>(() => Read(source, CompressionMode.None, 100, asynchronous));
        Assert.That(source.Position, Is.EqualTo(101));
        Assert.That(source.LargestRead, Is.EqualTo(101));
    }

    [TestCase(CompressionMode.Brotli)]
    [TestCase(CompressionMode.Deflate)]
    [TestCase(CompressionMode.GZip)]
    public void High_expansion_is_rejected_without_materializing_the_payload(CompressionMode mode)
    {
        var compressed = CompressionHelper.Compress(new byte[8 * 1024 * 1024], mode);
        using var source = new MemoryStream(compressed);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => CompressionHelper.Decompress(source, mode, 128));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // A broad allocation bound detects full 8 MiB expansion, not timing or small runtime changes.
        Assert.That(allocated, Is.LessThan(2 * 1024 * 1024));
    }

    [Test]
    public void Truncated_payload_is_rejected(
        [Values(CompressionMode.Brotli, CompressionMode.Deflate, CompressionMode.GZip)] CompressionMode mode,
        [Values] bool asynchronous)
    {
        var bytes = new byte[100_000];
        new Random(42).NextBytes(bytes);
        var compressed = CompressionHelper.Compress(bytes, mode);
        using var source = new MemoryStream(compressed, 0, compressed.Length / 2, writable: false);
        Assert.ThrowsAsync<InvalidDataException>(() => Read(source, mode, bytes.Length, asynchronous));
    }

    [Test]
    public void Cancellation_is_checked_before_reading_even_for_empty_input([Values] CompressionMode mode, [Values] bool asynchronous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var source = new ObservedStream([]);
        Assert.That(async () => await Read(source, mode, 0, asynchronous, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Reads, Is.Zero);
    }

    [Test]
    public void Cancellation_during_reading_stops_decompression_and_leaves_source_open([Values] CompressionMode mode, [Values] bool asynchronous)
    {
        using var cancellation = new CancellationTokenSource();
        var compressed = CompressionHelper.Compress(new byte[100_000], mode);
        using var source = new ObservedStream(compressed, cancellation.Cancel);
        Assert.That(async () => await Read(source, mode, 100_000, asynchronous, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Reads, Is.GreaterThan(0));
        Assert.That(source.CanRead, Is.True);
    }

    [Test]
    public void Invalid_length_and_mode_are_rejected_without_reading([Values] bool asynchronous)
    {
        using var source = new ObservedStream([]);
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Read(source, CompressionMode.None, -1, asynchronous));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Read(source, (CompressionMode)100, 0, asynchronous));
        Assert.That(source.Reads, Is.Zero);
    }

    private static Task<byte[]> Read(Stream source, CompressionMode mode, int length, bool asynchronous, CancellationToken ct = default) =>
        asynchronous ? CompressionHelper.DecompressAsync(source, mode, length, ct)
            : Task.FromResult(CompressionHelper.Decompress(source, mode, length, ct));

    private sealed class ObservedStream(byte[] bytes, Action? onRead = null) : Stream
    {
        private readonly MemoryStream _source = new(bytes, writable: false);
        public int Reads { get; private set; }
        public int LargestRead { get; private set; }
        public override bool CanRead => _source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _source.Length;
        public override long Position { get => _source.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            LargestRead = Math.Max(LargestRead, buffer.Length);
            var count = _source.Read(buffer);
            onRead?.Invoke();
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _source.Dispose();
            base.Dispose(disposing);
        }
    }
}
