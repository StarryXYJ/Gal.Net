using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Executes device-independent player actions through the existing game commands.</summary>
public sealed class GameActionDispatcher(
    PlayerInputSettings settings,
    Func<GamePresentationState?> activeGame,
    IGameSessionService session,
    Func<string, Task>? applicationAction = null)
{
    private GamePresentationState? _heldGame;
    private bool _busy;

    public bool CanReceive => session.IsPlaying && !settings.IsCapturing && !_busy &&
        activeGame() is { IsInteractionBlocked: false, IsChoiceVisible: false, IsSkipConfirmationOpen: false, IsHistoryOpen: false };

    public async Task DispatchAsync(string action)
    {
        if (!CanReceive || !settings.IsActionAvailable(action)) return;
        var game = activeGame()!;
        _busy = true;
        try
        {
            switch (action)
            {
                case "advance": game.CancelPlayback(); game.AdvanceCommand.Execute(null); break;
                case "hold.quick": _heldGame = game; await game.SetQuickHeldAsync(true); break;
                case "toggle.quick": await game.ToggleQuickCommand.ExecuteAsync(null); break;
                case "toggle.auto": await game.ToggleAutoCommand.ExecuteAsync(null); break;
                case "skip.unread": game.RequestSkipCommand.Execute(null); break;
                case "toggle.ui": game.CancelPlayback(); game.IsUiHidden = !game.IsUiHidden; break;
                case "open.save":
                case "open.load":
                case "open.settings":
                    game.CancelPlayback();
                    if (applicationAction is not null) await applicationAction(action);
                    break;
                case "screenshot": game.RequestScreenshotCommand.Execute(null); break;
                case "toggle.fullscreen": if (settings.ToggleFullscreenAsync is { } fullscreen) await fullscreen(); break;
                case "open.history": if (settings.OpenHistoryAsync is { } history) { game.CancelPlayback(); await history(); } break;
                case "save.first-empty":
                    game.CancelPlayback();
                    if (session is IFirstEmptySlotSession saves && await saves.SaveFirstEmptyAsync() is null &&
                        ReferenceEquals(game, activeGame()) && applicationAction is not null)
                        await applicationAction("open.save");
                    break;
            }
        }
        catch (Exception error) { game.StatusMessage = error.Message; }
        finally { _busy = false; }
    }

    public void ReleaseHold()
    {
        var held = _heldGame;
        _heldGame = null;
        held?.CancelPlayback();
    }
}
