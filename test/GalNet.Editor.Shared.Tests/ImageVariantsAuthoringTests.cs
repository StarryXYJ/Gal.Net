using GalNet.Contracts.Builtins.Assets;
using System.Text.Json;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Editor.Shared.Services;

namespace GeneralTest.Editor;

public sealed class ImageVariantsAuthoringTests
{
    private string _root = null!;
    private readonly IResourceTypeCatalog _types = BuiltinResourceTypes.CreateCatalog();
    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "galnet-atlas-author-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets", "alice"));
        foreach (var key in new[] { "idle_coat", "idle_skirt", "smile_coat", "smile_skirt", "weap" }) Put(key + ".png");
    }
    [TearDown] public void TearDown() => Directory.Delete(_root, true);
    private void Put(string name, string type = "sprite")
    {
        var path = Path.Combine(_root, "Assets", "alice", name); File.WriteAllText(path, "image");
        File.WriteAllText(path + ".meta", JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), type }));
    }
    [Test]
    public void CreateAndScanInferStableDimensionsDefaultAndAllValuesWithoutWriting()
    {
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var json = ImageVariantsAuthoring.Create(_root, "Assets/alice/figure.variants", _types);
        var snapshot = ImageVariantsAuthoring.Scan(_root, "Assets/alice/figure.variants", json, _types);
        Assert.That(snapshot.Build.Success, Is.True); Assert.That(snapshot.Source.Default, Is.EqualTo("idle_coat"));
        Assert.That(snapshot.Source.Dimensions, Is.Null);
        Assert.That(snapshot.Build.Definition!.Dimensions[0].Options, Is.EquivalentTo(new[] { "idle", "smile", "weap" }));
        Assert.That(snapshot.Build.Definition.Dimensions[1].Options, Is.EquivalentTo(new[] { "coat", "skirt" }));
        foreach (var pair in before) Assert.That(File.ReadAllBytes(pair.Key), Is.EqualTo(pair.Value));
        Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Has.Length.EqualTo(before.Count));
    }
    [Test]
    public void InvalidDefaultRemainsEditableAndReportsTheSameBuilderDiagnostics()
    {
        var snapshot = ImageVariantsAuthoring.Scan(_root, "Assets/figure.variants", """{"folder":"alice","default":"missing"}""", _types);
        Assert.That(snapshot.Source.Default, Is.EqualTo("missing")); Assert.That(snapshot.Files, Has.Count.EqualTo(5));
        Assert.That(snapshot.Build.Success, Is.False); Assert.That(snapshot.Build.Diagnostics, Is.Not.Empty);
    }
    [TestCase("""{"folder":"../outside","default":"idle_coat"}""")]
    [TestCase("""{"folder":"alice","default":"idle_coat","unknown":true}""")]
    [TestCase("""{"folder":"alice","folder":"other","default":"idle_coat"}""")]
    public void UnsafeOrAmbiguousDescriptionsAreRejectedWithoutRepair(string json)
        => Assert.Catch(() => ImageVariantsAuthoring.Scan(_root, "Assets/figure.variants", json, _types));
    [Test]
    public void MissingSidecarsAreNeverCreatedByInspection()
    {
        var path = Path.Combine(_root, "Assets/alice/weap.png.meta"); File.Delete(path);
        Assert.Throws<InvalidDataException>(() => ImageVariantsAuthoring.Scan(_root, "Assets/figure.variants", """{"folder":"alice","default":"idle_coat"}""", _types));
        Assert.That(File.Exists(path), Is.False);
    }
    [Test]
    public void DuplicateStemsAcrossImageExtensionsRejectCreation()
    {
        Put("idle_coat.jpg");
        Assert.Throws<InvalidDataException>(() => ImageVariantsAuthoring.Create(_root, "Assets/alice/figure.variants", _types));
    }
    [TestCase("Assets/../notes/figure.variants")]
    [TestCase("notes/figure.variants")]
    public void DescriptorCannotRedirectTheAssetsNamespace(string path)
        => Assert.Catch(() => ImageVariantsAuthoring.Scan(_root, path, """{"folder":".","default":"idle_coat"}""", _types));
}
