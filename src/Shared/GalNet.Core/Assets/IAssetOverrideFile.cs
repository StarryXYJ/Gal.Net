namespace GalNet.Core.Assets;

/// <summary>Runtime provenance of a logical file explicitly resolved through a resource override.</summary>
public interface IAssetOverrideFile
{
    bool IsResourceOverride { get; }
}
