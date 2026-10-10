using GalNet.Core.I18n;

namespace GalNet.Runtime.Content;

/// <summary>Host-ready text sources and language-independent character metadata.</summary>
public sealed class GameLocalizationContent
{
    public CharacterCatalog Characters { get; init; } = CharacterCatalog.Empty;
    public IReadOnlyList<GameLocalizationSource> Sources { get; init; } = [];
    public IReadOnlyList<GameLocalizationSource> ExternalSources { get; init; } = [];
    public IReadOnlyList<GameLocalizationDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>One source document; hosts load each document into their localization provider in order.</summary>
public sealed record GameLocalizationSource(string Culture, string RelativePath, string Json);

public sealed record GameLocalizationDiagnostic(string RelativePath, string Message);
