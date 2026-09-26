using GalNet.Core.Gallery;

namespace GeneralTest.Gallery;

public sealed class GalleryCatalogTests
{
    [Test]
    public void CatalogNormalizesTypesAndGroupsItemsWithoutMutatingTheSource()
    {
        var configuration = new GalleryConfiguration
        {
            Types =
            [
                new GalleryTypeRegistration { TypeId = " CG ", ResourceTypeName = " Sprite " },
                new GalleryTypeRegistration { TypeId = "Audio", ResourceTypeName = "AUDIO" }
            ],
            Items =
            [
                new GalleryItem { Id = "Opening_01", TypeId = "CG", ResourceId = " image-id ", Title = " Opening " },
                new GalleryItem { Id = "Theme", TypeId = "audio", ResourceId = "audio-id" }
            ]
        };

        var catalog = GalleryCatalog.Create(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Types.Select(type => (type.TypeId, type.ResourceTypeName)),
                Is.EqualTo(new[] { ("cg", "sprite"), ("audio", "audio") }));
            Assert.That(catalog.GetItems("CG").Single().Id, Is.EqualTo("opening_01"));
            Assert.That(catalog.GetItems("cg").Single().ResourceId, Is.EqualTo("image-id"));
            Assert.That(catalog.GetItems("cg").Single().Title, Is.EqualTo("Opening"));
            Assert.That(configuration.Types[0].TypeId, Is.EqualTo(" CG "));
            Assert.That(configuration.Items[0].Id, Is.EqualTo("Opening_01"));
        });
    }

    [Test]
    public void CatalogRejectsDuplicateTypeIdsAfterNormalization()
    {
        var configuration = new GalleryConfiguration
        {
            Types =
            [
                new GalleryTypeRegistration { TypeId = "CG", ResourceTypeName = "sprite" },
                new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" }
            ]
        };

        Assert.That(() => GalleryCatalog.Create(configuration), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CatalogRejectsUnknownTypesAndVariableUnsafeItemIds()
    {
        var unknownType = new GalleryConfiguration
        {
            Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" }],
            Items = [new GalleryItem { Id = "opening", TypeId = "video", ResourceId = "movie" }]
        };
        var unsafeId = new GalleryConfiguration
        {
            Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeName = "sprite" }],
            Items = [new GalleryItem { Id = "opening-movie", TypeId = "cg", ResourceId = "movie" }]
        };

        Assert.Multiple(() =>
        {
            Assert.That(() => GalleryCatalog.Create(unknownType), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => GalleryCatalog.Create(unsafeId), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void CatalogProvidesStableItemLookup()
    {
        var catalog = GalleryCatalog.Create(new GalleryConfiguration
        {
            Types = [new GalleryTypeRegistration { TypeId = "video", ResourceTypeName = "video" }],
            Items = [new GalleryItem { Id = "opening", TypeId = "video", ResourceId = "video-id" }]
        });

        Assert.Multiple(() =>
        {
            Assert.That(catalog.TryGetType("VIDEO", out var type), Is.True);
            Assert.That(type.ResourceTypeName, Is.EqualTo("video"));
            Assert.That(catalog.TryGetItem("OPENING", out var item), Is.True);
            Assert.That(item.ResourceId, Is.EqualTo("video-id"));
        });
    }
}
