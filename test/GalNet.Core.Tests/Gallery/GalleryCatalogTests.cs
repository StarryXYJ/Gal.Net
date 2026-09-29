using GalNet.Core.Gallery;

namespace GeneralTest.Gallery;

public sealed class GalleryCatalogTests
{
    [Test]
    public void Catalog_normalizes_types_and_orders_numeric_items()
    {
        var catalog = GalleryCatalog.Create(new GalleryConfiguration
        {
            Types =
            [
                new GalleryTypeRegistration { TypeId = " CG ", ResourceTypeId = " Sprite " },
                new GalleryTypeRegistration { TypeId = "Audio", ResourceTypeId = "AUDIO" }
            ],
            Items =
            [
                new GalleryItem { Id = 20, TypeId = "CG", ResourceId = " image-id ", Title = " Opening " },
                new GalleryItem { Id = 10, TypeId = "cg", ResourceId = "opening-2" }
            ]
        });

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Types.Select(type => (type.TypeId, type.ResourceTypeId)), Is.EqualTo(new[] { ("audio", "audio"), ("cg", "sprite") }));
            Assert.That(catalog.GetItems("cg").Select(item => item.Id), Is.EqualTo([10, 20]));
            Assert.That(catalog.GetItems("cg")[1].ResourceId, Is.EqualTo("image-id"));
            Assert.That(catalog.TryGetItem(20, out var item), Is.True);
            Assert.That(item.Title, Is.EqualTo("Opening"));
        });
    }

    [Test]
    public void Catalog_rejects_duplicate_types_and_non_positive_item_ids()
    {
        var duplicateType = new GalleryConfiguration
        {
            Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeId = "sprite" }, new GalleryTypeRegistration { TypeId = "CG", ResourceTypeId = "sprite" }]
        };
        var invalidIds = new GalleryConfiguration
        {
            Types = [new GalleryTypeRegistration { TypeId = "cg", ResourceTypeId = "sprite" }],
            Items = [new GalleryItem { Id = 0, TypeId = "cg", ResourceId = "a" }]
        };

        Assert.That(() => GalleryCatalog.Create(duplicateType), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => GalleryCatalog.Create(invalidIds), Throws.TypeOf<InvalidDataException>());
    }
}
