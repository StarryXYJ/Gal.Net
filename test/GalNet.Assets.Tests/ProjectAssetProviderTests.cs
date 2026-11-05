using System.Text.Json;
using GalNet.Assets.Provider;
using GalNet.Contracts.Builtins.Assets;

namespace GalNet.Assets.Tests;

public sealed class ProjectAssetProviderTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task TwoRootsAreCombinedAndAmbiguitiesAreRejected(bool duplicatePath)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GalNet.slnx"))) directory = directory.Parent!;
        var root = Path.Combine(directory.FullName, "output", "project-asset-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            void Add(string folder, string path, string id)
            {
                var file = Path.Combine(root, folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, folder);
                File.WriteAllText(file + ".meta", JsonSerializer.Serialize(new { id, type = "data", path }));
            }
            Add("Assets", "a.dat", "11111111111111111111111111111111");
            Add("Buildin", "b.dat", "22222222222222222222222222222222");
            using var provider = new ProjectAssetProvider(root, BuiltinResourceTypes.CreateCatalog());
            using (var archive = await provider.OpenArchiveAsync("assets"))
            {
                Assert.That(archive.AssetIds.Count(), Is.EqualTo(2));
                Assert.That(archive.GetAssetByPath("b.dat")!.ReadAllBytes(), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("Buildin")));
            }
            // New provider avoids depending on filesystem-watcher timing for the second snapshot.
            Add("Buildin", duplicatePath ? "A.dat" : "c.dat", duplicatePath ? "33333333333333333333333333333333" : "11111111-1111-1111-1111-111111111111");
            using var conflicting = new ProjectAssetProvider(root, BuiltinResourceTypes.CreateCatalog());
            var error = Assert.ThrowsAsync<InvalidDataException>(async () => { using var _ = await conflicting.OpenArchiveAsync("assets"); });
            Assert.That(error!.Message, Does.Contain("Assets/").And.Contain("Buildin/"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
