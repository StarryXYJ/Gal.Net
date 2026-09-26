using GalNet.Core.Gallery;
using GalNet.Editor.History;

namespace GalNet.Editor.ViewModels;

public partial class EditorWorkspaceViewModel
{
    public event Action? GalleryChanged;

    public IReadOnlyList<GalleryTypeRegistration> GetGalleryTypesForResource(string resourceTypeName)
    {
        var normalized = (resourceTypeName ?? "").Trim().ToLowerInvariant();
        return _documentService.CurrentDocument.Gallery.Types
            .Where(type => string.Equals(type.ResourceTypeName, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public GalleryItem? GetGalleryItem(string resourceId, string typeId) =>
        _documentService.CurrentDocument.Gallery.Items.FirstOrDefault(item =>
            string.Equals(item.ResourceId, resourceId, StringComparison.Ordinal)
            && string.Equals(item.TypeId, typeId, StringComparison.OrdinalIgnoreCase));

    public bool SetGalleryAnnotation(
        string resourceId,
        string typeId,
        bool isIncluded,
        string? title,
        int? sortOrder)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
            return false;

        var before = CloneGallery(_documentService.CurrentDocument.Gallery);
        var after = CloneGallery(before);
        var index = after.Items.FindIndex(item =>
            string.Equals(item.ResourceId, resourceId, StringComparison.Ordinal)
            && string.Equals(item.TypeId, typeId, StringComparison.OrdinalIgnoreCase));

        if (!isIncluded)
        {
            if (index < 0) return false;
            after.Items.RemoveAt(index);
        }
        else
        {
            var existing = index >= 0 ? after.Items[index] : null;
            var item = new GalleryItem
            {
                Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                TypeId = typeId,
                ResourceId = resourceId,
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                SortOrder = sortOrder
            };
            if (index < 0) after.Items.Add(item);
            else after.Items[index] = item;
        }

        GalleryCatalog.Create(after);
        ReplaceGallery(after);
        PushGraphEdit(new DelegateEdit("Edit Gallery annotation",
            () => ReplaceGallery(before),
            () => ReplaceGallery(after)));
        return true;
    }

    private void ReplaceGallery(GalleryConfiguration configuration)
    {
        _documentService.CurrentDocument.Gallery = CloneGallery(configuration);
        OnPropertyChanged(nameof(AllProjectVariableDefinitions));
        VariableDefinitionsChanged?.Invoke();
        GalleryChanged?.Invoke();
    }
}
