using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Input;

/// <summary>Executes device-independent player actions through the existing game commands.</summary>
public sealed class GameActionDispatcher(
    PlayerInputSettings settings,
    Func<GamePageViewModel?> activeGame,
    IGameSessionService session)
{
    private GamePageViewModel? _heldGame;
    private bool _busy;

    public bool CanReceive => session.IsPlaying && !settings.IsCapturing && !_busy &&
        activeGame() is { IsChoiceVisible: false, IsSkipConfirmationOpen: false, IsHistoryOpen: false };

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
                case "open.save": game.CancelPlayback(); await game.OpenSaveSlotsCommand.ExecuteAsync(null); break;
                case "open.load": game.CancelPlayback(); await game.OpenLoadSlotsCommand.ExecuteAsync(null); break;
                case "open.settings": game.CancelPlayback(); game.OpenSettingsCommand.Execute(null); break;
                case "screenshot": game.RequestScreenshotCommand.Execute(null); break;
                case "toggle.fullscreen": if (settings.ToggleFullscreenAsync is { } fullscreen) await fullscreen(); break;
                case "open.history": if (settings.OpenHistoryAsync is { } history) { game.CancelPlayback(); await history(); } break;
                case "save.first-empty":
                    game.CancelPlayback();
                    if (session is IFirstEmptySlotSession saves && await saves.SaveFirstEmptyAsync() is null && ReferenceEquals(game, activeGame()))
                        await game.OpenSaveSlotsCommand.ExecuteAsync(null);
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
