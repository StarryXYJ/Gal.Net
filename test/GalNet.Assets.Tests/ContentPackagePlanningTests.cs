using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Assets.Tests;

public sealed class ContentPackagePlanningTests
{
    private static readonly IResourceTypeCatalog Types = BuiltinResourceTypes.CreateCatalog();
    private static IGameFile[] Files() =>
    [
        Dependent(StoryResourceIds.Graph, "graph", [new("base", "group"), new("a", "group", "A"), new("b", "group", "B")]),
        Dependent("base", "group", [new("shared-base", "sprite")]),
        Dependent("a", "group", [new("shared-base", "sprite"), new("shared-extra", "sprite"), new("a-only", "sprite")]),
        Dependent("b", "group", [new("shared-base", "sprite"), new("shared-extra", "sprite")]),
        Sprite("shared-base"), Sprite("shared-extra"), Sprite("a-only"), Sprite("unused")
    ];

    [Test]
    public void OptionalPackagesOnlySubtractBaseAndNeverEachOther()
    {
        var plan = ContentPackagePlanner.Plan(Files(), [], Types, 1000);
        Assert.That(plan.Paks.Single(part => part.PackageId == "").Files.Select(file => file.Id), Is.EquivalentTo(new[] { StoryResourceIds.Graph, "base", "shared-base" }));
        Assert.That(plan.Paks.Single(part => part.PackageId == "A").Files.Select(file => file.Id), Is.EquivalentTo(new[] { "a", "shared-extra", "a-only" }));
        Assert.That(plan.Paks.Single(part => part.PackageId == "B").Files.Select(file => file.Id), Is.EquivalentTo(new[] { "b", "shared-extra" }));
        Assert.That(plan.Unused.Select(file => file.Id), Is.EqualTo(new[] { "unused" }));
    }

    [Test]
    public void SoftThresholdKeepsOversizedAssetsWholeAndInputOrderDoesNotChangeLayout()
    {
        var first = ContentPackagePlanner.Plan(Files(), [], Types, 1);
        var second = ContentPackagePlanner.Plan(Files().Reverse().ToArray(), [], Types, 1);
        Assert.That(first.Paks.All(part => part.Files.Count == 1), Is.True);
        Assert.That(first.Paks.Select(part => (part.PackageId, part.PartIndex, part.PartCount, part.Files[0].Id)),
            Is.EqualTo(second.Paks.Select(part => (part.PackageId, part.PartIndex, part.PartCount, part.Files[0].Id))));
    }

    [Test]
    public void BaseCannotPullOptionalStoryBodyThroughAnOrdinaryDependency()
    {
        var files = Files();
        ((DependentAssetMeta)files.Single(file => file.Id == "base").Metadata).Dependencies.Add(new("a", "group"));
        Assert.Throws<InvalidDataException>(() => ContentPackagePlanner.Plan(files, [], Types, 1000));
    }

    [Test]
    public void UnknownDynamicCoverageMustBeDeclaredAndAdditionalRootsAreExplicit()
    {
        var files = Files().Append(new GameFile("dynamic", "dynamic.bin", "data", new AssetMeta { Id = "dynamic", TypeId = "data", Path = "dynamic.bin" }, [1])).ToArray();
        ((DependentAssetMeta)files.Single(file => file.Id == "base").Metadata).Dependencies.Add(new("dynamic"));
        Assert.Throws<InvalidDataException>(() => ContentPackagePlanner.Plan(files, [], Types, 1000));
        var plan = ContentPackagePlanner.Plan(files, ["unused"], Types, 1000, ["dynamic"]);
        Assert.That(plan.Unused, Is.Empty);
        Assert.That(plan.Paks.Single(part => part.PackageId == "").Files.Any(file => file.Id == "unused"), Is.True);
    }

    [Test]
    public void NamesSupportUnicodeAndRejectCaseAliasesAndWhitespace()
    {
        var files = Files();
        var graph = (DependentAssetMeta)files[0].Metadata;
        graph.Dependencies[1] = new("a", "group", "番外 A");
        Assert.That(ContentPackagePlanner.Plan(files, [], Types, 1000).Paks.Any(part => part.PackageId == "番外 A"), Is.True);
        graph.Dependencies[1] = new("a", "group", "b");
        Assert.Throws<InvalidDataException>(() => ContentPackagePlanner.Plan(files, [], Types, 1000));
        graph.Dependencies[1] = new("a", "group", " A ");
        Assert.Throws<InvalidDataException>(() => ContentPackagePlanner.Plan(files, [], Types, 1000));
    }

    private static GameFile Dependent(string id, string type, List<AssetDependency> dependencies) => new(id,
        Uri.EscapeDataString(id) + ".json", type, new DependentAssetMeta { Id = id, Path = Uri.EscapeDataString(id) + ".json",
            TypeId = type, DependenciesComplete = true, Dependencies = dependencies }, [1, 2]);
    private static GameFile Sprite(string id) => new(id, id + ".png", "sprite",
        new SpriteAssetMeta { Id = id, Path = id + ".png", TypeId = "sprite" }, [1, 2]);
}
