using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Inspector.ViewModels;
public sealed class PreviewVariablesInspectorControlViewModel : ObservableObject, IInspectorControlViewModel
{
    public GamePreviewPanelViewModel Preview { get; }
    public bool IsAvailable => true;
    public PreviewVariablesInspectorControlViewModel(GamePreviewPanelViewModel preview) => Preview = preview;
    public void Dispose() { }
}
