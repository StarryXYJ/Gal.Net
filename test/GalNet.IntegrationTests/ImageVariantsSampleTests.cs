using GalNet.Contracts.Builtins.Assets;
using System.Diagnostics;
using System.Text.Json;
using GalNet.Assets;
using GalNet.Assets.Provider;
using GalNet.Assets.Variants;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Contracts.Builtins.Variants;
using GalNet.Editor.Shared.Services;
using GalNet.Presentation.Abstractions.Execution;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public sealed class ImageVariantsSampleTests
{
    private const string Portrait = "a1000000000000000000000000000020";
    private const string Classroom = "a1000000000000000000000000000021";

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public async Task Exported_sample_runs_without_source_images_and_continues_from_both_fallback_snapshots(PrimitiveInvocationMode mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-variants-sample-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(FindRepository(), "GameTestCase");
            var types = BuiltinResourceTypes.CreateCatalog();
            var gallery = BuiltinGalleryTypes.CreateCatalog(types);
            var export = await GamePackageExporter.ExportAsync("variants-sample", "Variants", source, root,
                types, gallery, new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
            Assert.That(export.Success, Is.True, export.Error);
            var installation = await GameInstallation.OpenAsync(export.PackagePath!);
            Assert.That(Directory.GetFiles(Path.Combine(installation.RootDirectory, "Assets"), "*", SearchOption.AllDirectories)
                .Select(Path.GetExtension), Is.All.EqualTo(".pak"));
            var content = await installation.CreateContentProvider(types, gallery).LoadAsync();
            using var assets = new AssetManager(installation.CreateAssetProviders(types));
            assets.RegisterImageVariantsDecoder();
            var catalog = await assets.LoadImageVariantsCatalogAsync();
            using var local = new AssetManager([new LocalFileProvider(Path.Combine(source, "Assets"), types)]);
            local.RegisterImageVariantsDecoder();
            var localCatalog = await local.LoadImageVariantsCatalogAsync();
            foreach (var id in new[] { Portrait, Classroom })
            {
                Assert.That(ImageVariantsCodec.Encode(catalog.Get(id)), Is.EqualTo(ImageVariantsCodec.Encode(localCatalog.Get(id))));
                foreach (var variant in catalog.Get(id).Variants)
                {
                    using var packed = await assets.AcquireAsync<byte[]>(variant.AssetId);
                    using var original = await local.AcquireAsync<byte[]>(variant.AssetId);
                    Assert.That(packed!.Value, Is.EqualTo(original!.Value), variant.Key);
                }
            }
            using var view = new GameRegistration().AddBuiltins(imageVariants: catalog).BuildView();
            var runtime = new GameRuntime(null, "intro", stateCatalog: view.StateCatalog);
            var entries = content.Graph.Nodes.OfType<Group>().Single(group => group.Id == "intro").Entries.OfType<PrimitiveEntry>().ToArray();
            void Entry(string id) => view.Dispatch(entries.Single(entry => entry.StableId == id + "#1"), runtime, default, mode);
            void Select(string handle, string dimension, string value) => view.Dispatch(new PrimitiveEntry("variants.select",
                JsonSerializer.SerializeToElement(new { handleId = handle, dimension, value })), runtime, default, mode);
            Layer Layer(string handle) => runtime.State.Table<Layer>().Get(handle);
            async Task Restore()
            {
                runtime.RestoreFrom(runtime.CreateSnapshot());
                await using var restorer = new StateRestoreSession(view);
                await restorer.RestoreAsync(runtime);
            }

            Entry("background");
            Entry("portrait");
            view.Dispatch(new PrimitiveEntry("variants.show", JsonSerializer.SerializeToElement(new
            { handleId = "second", assetId = Portrait })), runtime, default, mode);
            Entry("variants-shy");
            Entry("variants-solo");
            Assert.That(Layer("teacher").Variants!.Reason, Is.EqualTo(ImageVariantResolutionReason.Reduced));
            Assert.That(Layer("teacher").Variants!.Request["expression"], Is.EqualTo("shy"));
            await Restore();
            Entry("variants-return");
            Assert.That(Layer("teacher").Variants!.VariantKey, Is.EqualTo("xy_shy"));
            Assert.That(Layer("second").Variants!.VariantKey, Is.EqualTo("xy_idle"));
            Entry("classroom-speech");
            Assert.That(Layer("background").Variants!.Reason, Is.EqualTo(ImageVariantResolutionReason.Default));
            Assert.That(Layer("background").AssetId, Is.EqualTo("a1000000000000000000000000000001"));
            await Restore();
            Entry("classroom-teacher");
            Assert.That(Layer("background").Variants!.VariantKey, Is.EqualTo("classroom_speech_teacher"));
            var before = Layer("teacher");
            view.Dispatch(new PrimitiveEntry("layer.move", JsonSerializer.SerializeToElement(new
            { handleId = "teacher", transform = new { x = 320, y = 90 }, z = 11, duration = 0 })), runtime, default, mode);
            Entry("portrait-jitter");
            Entry("variants-during-jitter");
            Assert.Multiple(() =>
            {
                Assert.That(Layer("teacher"), Is.SameAs(before));
                Assert.That(before.Transform.X, Is.EqualTo(320));
                Assert.That(before.Z, Is.EqualTo(11));
                Assert.That(before.Variants!.VariantKey, Is.EqualTo("xy_wink"));
                Assert.That(runtime.State.Table<LoopAnimationState>().Values, Has.Count.EqualTo(1));
            });
            Entry("stop-portrait-jitter");
            Entry("variants-hide");
            Assert.That(runtime.State.Table<Layer>().Values.Select(layer => layer.Id), Does.Not.Contain("teacher"));
            Entry("variants-reshow");
            Assert.That(Layer("teacher").Variants!.VariantKey, Is.EqualTo("xy_idle"));
            Select("teacher", "expression", "cutelyannoyed");
            Assert.That(Layer("teacher").Variants!.VariantKey, Is.EqualTo("xy_cutelyannoyed"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public void Large_definition_and_rapid_selection_report_reproducible_cpu_allocation_observations()
    {
        // Synthetic filenames/IDs measure indexing and resolution, not image decoding or GUI rendering.
        var files = (from pose in Enumerable.Range(0, 16)
                     from expression in Enumerable.Range(0, 16)
                     from outfit in Enumerable.Range(0, 16)
                     select new ImageVariantFile($"p{pose}_e{expression}_o{outfit}.png", $"sprite-{pose}-{expression}-{outfit}")).ToArray();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var definition = ImageVariantsBuilder.Build(new() { Folder = ".", Default = "p0_e0_o0" }, files).Definition!;
        var buildMs = clock.Elapsed.TotalMilliseconds;
        var buildBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var result = ImageVariantsResolver.Initialize(definition);
        allocated = GC.GetAllocatedBytesForCurrentThread();
        clock.Restart();
        for (var index = 0; index < 10000; index++)
            result = ImageVariantsResolver.Select(definition, result.Request, "d2", "e" + index % 16);
        TestContext.Out.WriteLine(JsonSerializer.Serialize(new
        {
            variants = files.Length,
            selections = 10000,
            buildMs,
            buildAllocatedBytes = buildBytes,
            selectMs = clock.Elapsed.TotalMilliseconds,
            selectAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
        }));
        Assert.That(result.Variant.Key, Is.EqualTo("p0_e15_o0"));
        Assert.That(definition.Dimensions.Select(axis => axis.Options.Count), Is.EqualTo(new[] { 16, 16, 16 }));
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "GameTestCase"))) return directory.FullName;
        throw new DirectoryNotFoundException("GameTestCase not found.");
    }
}
