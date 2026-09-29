using System.Collections.ObjectModel;
using GalNet.Core.Gallery;
using GalNet.Core.Variable;
using GalNet.Runtime.Variables;
using GalVariable = GalNet.Core.Variable.Variable;

namespace GalNet.Runtime.Gallery;

/// <summary>Merges the immutable Gallery catalog with current Player variables.</summary>
public sealed class GalleryDataSource(GalleryCatalog catalog, IVariableService variables) : IGalleryDataSource
{
    public IReadOnlyList<GalleryTypeData> GetTypes()
    {
        var playerVariables = variables.GetSnapshot(VariableScope.Player);
        var result = catalog.Types
            .Select(type =>
            {
                var items = catalog.GetItems(type.TypeId)
                    .Select(item => new GalleryItemData(item, IsUnlocked(playerVariables, item)))
                    .ToList();
                return new GalleryTypeData(type, new ReadOnlyCollection<GalleryItemData>(items));
            })
            .Where(type => type.Items.Count > 0)
            .ToList();
        return new ReadOnlyCollection<GalleryTypeData>(result);
    }

    private static bool IsUnlocked(
        IReadOnlyDictionary<string, GalVariable> variables,
        GalleryItem item) =>
        variables.TryGetValue(GalleryUnlockVariable.GetName(item.Id), out var value)
        && value.Type == VariableType.Bool
        && value.AsBool();
}
