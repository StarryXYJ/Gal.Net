using System.Collections.ObjectModel;
using GalNet.Core.Variable;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Core.Gallery;

/// <summary>Defines the stable Player-variable contract used by Gallery unlock state.</summary>
public static class GalleryUnlockVariable
{
    public const string Owner = "gallery";
    public const string Prefix = "gallery_";
    public const string Suffix = "_unlocked";

    public static string GetName(string itemId)
    {
        var normalized = (itemId ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Any(character =>
                character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_'))
            throw new ArgumentException("Gallery item IDs must contain only letters, digits or '_'.", nameof(itemId));

        return $"{Prefix}{normalized}{Suffix}";
    }

    public static string GetRuntimeName(string itemId) => $"player.{GetName(itemId)}";

    public static bool IsReservedName(string? name)
    {
        if (string.IsNullOrEmpty(name)
            || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(Suffix, StringComparison.Ordinal))
            return false;

        var itemIdLength = name.Length - Prefix.Length - Suffix.Length;
        if (itemIdLength <= 0)
            return false;

        return IsValidMixedItemId(name.AsSpan(Prefix.Length, itemIdLength));
    }

    public static IReadOnlyList<SystemVariableDefinition> CreateDefinitions(GalleryCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var definitions = catalog.Items.Select(item =>
        {
            var name = GetName(item.Id);
            var defaultValue = new GalVariable { Name = name };
            defaultValue.SetValue(false);
            return new SystemVariableDefinition
            {
                Owner = Owner,
                Name = name,
                Scope = VariableScope.Player,
                DefaultValue = defaultValue
            };
        }).ToList();
        return new ReadOnlyCollection<SystemVariableDefinition>(definitions);
    }

    private static bool IsValidMixedItemId(ReadOnlySpan<char> itemId)
    {
        foreach (var character in itemId)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                continue;
            return false;
        }

        return true;
    }
}
