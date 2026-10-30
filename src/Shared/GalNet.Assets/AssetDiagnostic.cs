namespace GalNet.Assets;

public enum AssetLoadStage { Lookup, Read, Decode, Enumeration }
public enum AssetFailureKind { NotFound, InvalidData, ProviderFailure, ReadFailure, DecoderMissing, DecodeFailure }

/// <summary>A recoverable resource failure. IDs and paths are logical; SourceAssetId identifies an overlay's backing entry.
/// Exception is the original failure, when one was thrown. Cancellation is not reported.</summary>
public sealed record AssetDiagnostic(
    AssetLoadStage Stage, AssetFailureKind Kind, string? AssetId = null, string? Path = null,
    string? Provider = null, Type? TargetType = null, Exception? Exception = null, string? SourceAssetId = null);

/// <summary>Provenance retained by the frozen overlay without changing the logical resource identity.</summary>
internal interface IAssetFileSource
{
    string ProviderName { get; }
    string SourceAssetId { get; }
}
