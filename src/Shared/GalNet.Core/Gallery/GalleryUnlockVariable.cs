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

    public static string GetName(int itemId)
    {
        if (itemId <= 0) throw new ArgumentOutOfRangeException(nameof(itemId), "Gallery item IDs must be positive.");
        return $"{Prefix}{itemId}{Suffix}";
    }

    public static string GetRuntimeName(int itemId) => $"player.{GetName(itemId)}";

    public static bool IsReservedName(string? name)
    {
        if (string.IsNullOrEmpty(name)
            || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(Suffix, StringComparison.Ordinal))
            return false;

        var itemIdLength = name.Length - Prefix.Length - Suffix.Length;
        if (itemIdLength <= 0)
            return false;

        return int.TryParse(name.AsSpan(Prefix.Length, itemIdLength), out var itemId) && itemId > 0;
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

}
