using GalNet.Core.Assets;

namespace GeneralTest.Assets;

public sealed class ResourceCatalogTests
{
    public sealed class ProjectMeta : AssetMeta { public bool Enabled { get; set; } }
    [Test]
    public void DefaultMetadataTypeIsSelectedByTheCatalogWithoutBuiltinTypeNames()
    {
        var catalog = new ResourceTypeCatalogBuilder().Add<ProjectMeta>("project.resource").DefaultType("project.resource").Build();
        var codec = new AssetMetaCodec(catalog);
        var metadata = codec.Deserialize("""{"id":"item","enabled":true,"future":{"kept":true}}""", "file.unrecognized");
        Assert.That(metadata, Is.TypeOf<ProjectMeta>());
        Assert.That(((ProjectMeta)metadata).Enabled, Is.True);
        Assert.That(metadata.TypeId, Is.EqualTo("project.resource"));
        Assert.That(codec.Serialize(metadata), Does.Contain("kept"));
    }
    [Test]
    public void CatalogWithoutDefaultAcceptsExplicitTypesAndRejectsOnlyUnresolvedTypes()
    {
        var codec = new AssetMetaCodec(new ResourceTypeCatalogBuilder().Add<ProjectMeta>("project").Build());
        Assert.That(codec.Deserialize("""{"id":"item","type":"project"}""", "file.unknown"), Is.TypeOf<ProjectMeta>());
        Assert.Throws<InvalidDataException>(() => codec.Deserialize("""{"id":"item"}""", "file.unknown"));
    }
}
