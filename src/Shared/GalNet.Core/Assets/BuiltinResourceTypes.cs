namespace GalNet.Core.Assets;

public static class BuiltinResourceTypes
{
    public static IResourceTypeCatalog CreateCatalog() => new ResourceTypeCatalogBuilder()
        .Add<SpriteAssetMeta>("sprite", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".avif")
        .Add<AudioAssetMeta>("audio", ".mp3", ".wav", ".ogg", ".flac")
        .Add<VideoAssetMeta>("video", ".mp4", ".webm", ".mkv")
        .Add<FontAssetMeta>("font", ".ttf", ".otf", ".woff", ".woff2")
        .Add<EffectProgramAssetMeta>("effect-program", ".sksl")
        .Add<BinaryAssetMeta>("data")
        .Build();
}
