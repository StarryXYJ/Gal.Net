using GalNet.Assets;

namespace GalNet.Assets.Tests;

public sealed class FileSystemPathsTests
{
    [TestCase("../escape")]
    [TestCase("a/../escape")]
    [TestCase("a//file")]
    [TestCase("./file")]
    [TestCase("a\\file")]
    [TestCase("/absolute")]
    [TestCase("")]
    public void PackageFileNamesRejectTraversalAndNonCanonicalSegments(string path) =>
        Assert.That(() => FileSystemPaths.ResolveRelativeFile(Path.GetTempPath(), path), Throws.TypeOf<InvalidDataException>());

    [Test]
    public void ContainmentUsesPathSegmentsAndSupportsDriveRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-paths");
        Assert.That(FileSystemPaths.IsSameOrChild(root, root), Is.True);
        Assert.That(FileSystemPaths.IsSameOrChild(Path.Combine(root, "child"), root), Is.True);
        Assert.That(FileSystemPaths.IsSameOrChild(root + "-sibling", root), Is.False);
        Assert.That(FileSystemPaths.IsSameOrChild(Path.GetDirectoryName(root)!, root), Is.False);
        Assert.That(FileSystemPaths.IsSameOrChild(root.ToUpperInvariant(), root),
            Is.EqualTo(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()));
        Assert.That(FileSystemPaths.ResolveRelativeFile(Path.GetPathRoot(root)!, "folder/file"),
            Is.EqualTo(Path.Combine(Path.GetPathRoot(root)!, "folder", "file")));
    }

    [Test]
    public void MissingOutputCannotTraverseALinkedAncestor()
    {
        var root = Path.Combine(Path.GetTempPath(), "galnet-linked-path-" + Guid.NewGuid().ToString("N"));
        var real = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
        var link = Path.Combine(root, "link");
        try
        {
            DirectoryLinks.Create(link, real);
            Assert.That(() => FileSystemPaths.RequireNoLinks(Path.Combine(link, "missing", "file"), "linked"),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo($"linked Path: '{link}'."));
            Assert.That(() => FileSystemPaths.RequireNoLinks(Path.Combine(real, "missing", "file"), "linked"), Throws.Nothing);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }
}
