namespace GalNet.Core.Services;

/// <summary>
/// Controls what happens when the game requests to exit / quit.
/// Different hosts (Editor, Launcher, standalone) inject different implementations.
/// </summary>
public interface IGameExitService
{
    void Exit();
}
