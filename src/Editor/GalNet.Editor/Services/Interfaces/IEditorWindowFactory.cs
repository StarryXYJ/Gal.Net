using GalNet.Editor.Views;

namespace GalNet.Editor.Services.Interfaces;

public interface IEditorWindowFactory
{
    ProjectSettingsWindow CreateProjectSettingsWindow();
    EditorSettingsWindow CreateEditorSettingsWindow();
    ExportWindow CreateExportWindow();
    Task<string?> RequestLayoutNameAsync(GalNet.Editor.Abstraction.Services.IEditorLocalizationService localization);
}
