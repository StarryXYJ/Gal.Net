using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Entry;
using GalNet.Core.I18n;
using GalNet.Editor.Shared.Services;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class BundledGameInstallerTests
{
    private string _root = null!;
    private string Source => Path.Combine(_root, "source");
    private string Cache => Path.Combine(_root, "cache");

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-bundled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Source, "Graph/groups"));
        Directory.CreateDirectory(Path.Combine(Source, "I18n/en-US"));
        File.WriteAllText(Path.Combine(Source, "Graph/graph.json"),
            """{"version":2,"name":"Bundled","rootNodeId":"main","nodes":[{"id":"main","name":"Main","type":"Group","file":"groups/main.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
        File.WriteAllText(Path.Combine(Source, "Graph/groups/main.rawgalgroup"),
            """{"version":2,"kind":"Raw","entries":[{"id":"line","type":"dialogue.text","parameters":{"content":"line"}}]}""");
        File.WriteAllText(Path.Combine(Source, "settings.json"), JsonSerializer.Serialize(new DefaultProjectOptions
        { TargetLocale = I18nLocale.EnUs, AvailableLocales = [I18nLocale.EnUs] }.ToConfiguration()));
        File.WriteAllText(Path.Combine(Source, "I18n/en-US/main.json"), """{"line":"first"}""");
    }

    [TearDown] public void Cleanup() => Directory.Delete(_root, true);

    private async Task<byte[]> Export()
    {
        var types = BuiltinResourceTypes.CreateCatalog();
        var result = await GamePackageExporter.ExportAsync("bundled-project", "Bundled", Source,
            Path.Combine(_root, "export-" + Guid.NewGuid().ToString("N")), types, BuiltinGalleryTypes.CreateCatalog(types),
            new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()), DefaultProjectConfiguration.Schema);
        Assert.That(result.Success, Is.True, result.Error);
        return await File.ReadAllBytesAsync(result.PackagePath!);
    }

    [Test]
    public async Task NonSeekablePlatformAssetsHaveVersionedContentAndStableProjectProfiles()
    {
        var first = await Export();
        using var stream = new AssetStream(first);
        var installed = await BundledGameInstaller.InstallAsync(stream, Cache);
        Assert.That(stream.CanRead, Is.True, "The platform owns its asset stream.");
        using var repeated = new AssetStream(first);
        Assert.That(await BundledGameInstaller.InstallAsync(repeated, Cache), Is.EqualTo(installed));
        var descriptor = InstalledGameDescriptor.Read(installed);
        Assert.That(descriptor.ProjectId, Is.EqualTo("bundled-project"));
        File.WriteAllText(Path.Combine(Source, "I18n/en-US/main.json"), """{"line":"updated"}""");
        using var updated = new AssetStream(await Export());
        var newer = await BundledGameInstaller.InstallAsync(updated, Cache);
        Assert.That(newer, Is.Not.EqualTo(installed));
        Assert.That(InstalledGameDescriptor.Read(newer).BuildId, Is.Not.EqualTo(descriptor.BuildId));
        var profiles = Path.Combine(_root, "profiles");
        Assert.That(GameProfilePaths.GetProfileDirectory(newer, profiles), Is.EqualTo(GameProfilePaths.GetProfileDirectory(installed, profiles)));
        Assert.That(Directory.GetFiles(Cache, ".incoming-*"), Is.Empty);
    }

    [Test]
    public async Task FailedAndCancelledAssetCopiesPreserveInstalledContentAndRemoveOnlyTheirIncomingFile()
    {
        var package = await Export();
        using var valid = new AssetStream(package);
        var installed = await BundledGameInstaller.InstallAsync(valid, Cache);
        var before = File.ReadAllBytes(Path.Combine(installed, "game.pak"));
        using var invalid = new AssetStream("broken archive"u8.ToArray());
        Assert.ThrowsAsync<InvalidDataException>(() => BundledGameInstaller.InstallAsync(invalid, Cache));
        using var cancellation = new CancellationTokenSource();
        using var cancelled = new AssetStream(package, cancellation);
        Assert.CatchAsync<OperationCanceledException>(() => BundledGameInstaller.InstallAsync(cancelled, Cache, cancellation.Token));
        Assert.That(File.ReadAllBytes(Path.Combine(installed, "game.pak")), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(Cache, ".incoming-*"), Is.Empty);
        Assert.That(Directory.GetDirectories(Cache), Is.EqualTo(new[] { installed }));
    }

    private sealed class AssetStream(byte[] bytes, CancellationTokenSource? cancel = null) : Stream
    {
        private readonly MemoryStream _input = new(bytes, writable: false);
        public override bool CanRead => _input.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _input.ReadAsync(buffer[..Math.Min(buffer.Length, 128)], cancellationToken);
            cancel?.Cancel();
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
    }
}
