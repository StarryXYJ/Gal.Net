using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Services;

public interface IGamePreviewPanelFactory
{
    GamePreviewPanelViewModel Create();
}
