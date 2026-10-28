using System.Text.Json;
using GalNet.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Shared.Tests;

public sealed class ResourceReferenceIndexTests
{
    [Test]
    public void StoryPackageSurvivesAuthorCloningAndIdentityPreservingReload()
    {
        var source = new EditorProjectDocument { Graph = new() { Nodes = [new() { Id = "main", Type = "Group", File = "groups/main.rawgalgroup", StoryPackage = "A" }] }, GroupEntries = new() { ["main"] = [] } };
        var clone = GalNet.Editor.Abstraction.Changes.EditorDocumentCloner.Clone(source);
        Assert.That(clone.Graph.Nodes[0].StoryPackage, Is.EqualTo("A"));
        var service = new EditorDocumentService();
        service.Load(new() { Document = source.Graph, GroupEntries = source.GroupEntries });
        var identity = service.CurrentDocument.Nodes[0]; clone.Graph.Nodes[0].StoryPackage = "B";
        service.Reload(new() { Document = clone.Graph, GroupEntries = clone.GroupEntries });
        Assert.That(service.CurrentDocument.Nodes[0], Is.SameAs(identity));
        Assert.That(identity.StoryPackage, Is.EqualTo("B"));
    }

    [Test]
    public void AllGroupsIncludingStoryAndIndirectResourcesRemainVisibleWithoutOpeningThem()
    {
        var document = new EditorProjectDocument
        {
            Graph = new() { Nodes = [new() { Id = "patch", Type = "Group", StoryPackage = "bonus" }] },
            GroupEntries = new()
            {
                ["patch"] = [new() { StableId = "show", Type = "variants.show",
                Parameters = new() { ["assetId"] = JsonSerializer.SerializeToElement(" ATLAS ") } }]
            }
        };
        var atlas = new DependentAssetMeta { Id = "atlas", TypeId = "image-variants", Path = "hero.variants", DependenciesComplete = true, Dependencies = [new("sprite", "sprite")] };
        var sprite = new SpriteAssetMeta { Id = "sprite", TypeId = "sprite", Path = "hero.png" };
        var files = new IGameFile[] { new GameFile("atlas", atlas.Path, atlas.TypeId, atlas, []), new GameFile("sprite", sprite.Path, sprite.TypeId, sprite, []) };
        var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
        var index = new ResourceReferenceIndex(document, catalog, files, new());
        Assert.That(index.Rows.Select(row => row.Id), Is.EquivalentTo(new[] { "atlas", "sprite" }));
        Assert.That(index.Rows.Single(row => row.Id == "sprite").Uses.Single().Through, Is.EqualTo("atlas"));
        Assert.That(index.Rows.All(row => row.Uses.Single().GroupId == "patch"), Is.True);
        var missing = new ResourceReferenceIndex(document, catalog, files.Take(1).ToArray(), new());
        Assert.That(missing.Rows.Single(row => row.Id == "sprite").Missing, Is.True);
        Assert.That(missing.Issues, Does.Contain("Missing resource: sprite"));
    }
}
