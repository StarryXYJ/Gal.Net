namespace GalNet.Core.I18n;

/// <summary>A stable character identity and the localization key for its display name.</summary>
public sealed record CharacterDefinition(string Id, string DisplayNameKey)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayNameKey);
    }
}

/// <summary>
/// Validated game character metadata. Localized display names are intentionally not
/// stored here; hosts resolve <see cref="CharacterDefinition.DisplayNameKey"/> through
/// the same text resolver used by dialogue.
/// </summary>
public sealed class CharacterCatalog
{
    private readonly Dictionary<string, CharacterDefinition> _characters;

    public static CharacterCatalog Empty { get; } = new([]);

    public CharacterCatalog(IEnumerable<CharacterDefinition> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        var values = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
        foreach (var character in characters)
        {
            ArgumentNullException.ThrowIfNull(character);
            character.Validate();
            if (!values.TryAdd(character.Id, character))
                throw new InvalidDataException($"Character ID '{character.Id}' is duplicated.");
        }

        _characters = values;
    }

    public IReadOnlyCollection<CharacterDefinition> Characters => _characters.Values;

    public bool TryGet(string id, out CharacterDefinition? character) =>
        _characters.TryGetValue(id, out character);

    public CharacterDefinition GetRequired(string id) =>
        TryGet(id, out var character)
            ? character!
            : throw new InvalidDataException($"Unknown character ID '{id}'.");
}
