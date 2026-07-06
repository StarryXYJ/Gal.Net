namespace GalNet.Editor.Services;

/// <summary>
/// Editor implementation: Quit does nothing — the embedded game stays on its page.
/// </summary>
public class EditorGameExitService : GalNet.Core.Services.IGameExitService
{
    public void Exit()
    {
        // No-op: Quit in editor preview does nothing
    }
}
