using GalNet.Core.I18n;

namespace GeneralTest.I18n;

public class CharacterCatalogTests
{
    [Test]
    public void Catalog_ResolvesStableIdToDisplayNameKey()
    {
        var catalog = new CharacterCatalog([new CharacterDefinition("alice", "character.alice.name")]);

        Assert.That(catalog.GetRequired("alice").DisplayNameKey, Is.EqualTo("character.alice.name"));
    }

    [Test]
    public void Catalog_RejectsDuplicateIds()
    {
        Assert.That(() => new CharacterCatalog(
        [
            new CharacterDefinition("alice", "character.alice.name"),
            new CharacterDefinition("alice", "character.alice.alt")
        ]), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("", "character.alice.name")]
    [TestCase("alice", " ")]
    public void Catalog_RejectsMissingIdentityOrNameKey(string id, string key)
    {
        Assert.That(() => new CharacterCatalog([new(id, key)]), Throws.ArgumentException);
    }
}
