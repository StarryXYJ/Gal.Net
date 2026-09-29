namespace GalNet.Editor.Shared.Services;

/// <summary>
/// Editor implementation: Quit does nothing — the embedded game stays on its page.
/// </summary>
public class EditorGameExitService : GalNet.Editor.Abstraction.Services.IGameExitService
{
    public void Exit()
    {
        // No-op: Quit in editor preview does nothing
    }
}
