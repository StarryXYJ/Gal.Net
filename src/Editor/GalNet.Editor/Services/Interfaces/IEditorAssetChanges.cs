namespace GalNet.Editor.Services.Interfaces;

/// <summary>Editor resource generations; paths and labels are attributes of an ID.</summary>
public interface IEditorAssetChanges
{
    long Revision { get; }
    event Action? Changed;
}
