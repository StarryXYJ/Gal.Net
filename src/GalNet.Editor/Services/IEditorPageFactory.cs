using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public interface IEditorPageFactory
{
    EditorPageViewModel CreateEditorPage();
}
