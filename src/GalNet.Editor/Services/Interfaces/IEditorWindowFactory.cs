using GalNet.Editor.Views;

namespace GalNet.Editor.Services;

public interface IEditorWindowFactory
{
    ProjectSettingsWindow CreateProjectSettingsWindow();
    EditorSettingsWindow CreateEditorSettingsWindow();
    ExportWindow CreateExportWindow();
}
