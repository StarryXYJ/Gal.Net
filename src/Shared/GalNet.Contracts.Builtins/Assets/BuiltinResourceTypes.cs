using GalNet.Contracts.Builtins.Assets;
using GalNet.Core.Assets;

namespace GalNet.Contracts.Builtins.Assets;

public static class BuiltinResourceTypes
{
    public static IResourceTypeCatalog CreateCatalog() => new ResourceTypeCatalogBuilder()
        .Add<SpriteAssetMeta>("sprite", BuiltinJson.Resolver, ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".avif")
        .Add<DependentAssetMeta>("image-variants", ".variants")
        .Add<DependentAssetMeta>("graph", ".galgraph")
        .Add<DependentAssetMeta>("group", ".galgroup")
        .Add<AssetMeta>("audio", ".mp3", ".wav", ".ogg", ".flac")
        .Add<AssetMeta>("video", ".mp4", ".webm", ".mkv")
        .Add<AssetMeta>("font", ".ttf", ".otf", ".woff", ".woff2")
        .Add<AssetMeta>("effect-program", ".sksl")
        .Add<AssetMeta>("data")
        .WithoutDependencies("sprite", "audio", "video", "font", "effect-program")
        .DefaultType("data").Build();
}
