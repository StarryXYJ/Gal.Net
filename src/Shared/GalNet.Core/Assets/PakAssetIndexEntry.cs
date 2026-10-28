namespace GalNet.Core.Assets;

/// <summary>Archive table facts without decoding the resource payload.</summary>
public sealed record PakAssetIndexEntry(string Id, string Path, string TypeId, AssetMeta Metadata, long Length, string Hash);
