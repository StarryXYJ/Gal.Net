using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Page;
using GalNet.Core.View;
using GalNet.Core.Scene;
using GalNet.Game.Controls;
using GalNet.Avalonia.GameView.ViewModels;

namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Host-provided creation of layer visuals; the shared page never resolves files itself.</summary>
public interface IGamePageLayerFactory
{
    IImage? ResolveLayerImage(string assetId);
}

/// <summary>Maps runtime layer, dialogue and interaction ports onto a shared <see cref="GamePage"/>.</summary>
public sealed class AvaloniaGamePageView : ILayerView, IControlView, ITypewriterView, IInteractionView, IDisposable
{
    private readonly GamePageViewModel _state;
    private readonly GamePage _page;
    private readonly IGamePageLayerFactory _layers;
    private bool _isTyping;

    public AvaloniaGamePageView(GamePageViewModel state, GamePage page, IGamePageLayerFactory layers)
    {
        _state = state;
        _page = page;
        _layers = layers;
        _state.AdvanceRequested += Advance;
    }

    public void ShowLayer(LayerRenderRequest request) => OnUi(() =>
        _state.SetLayer(request.HandleId, new SceneLayerItem
        {
            HandleId = request.HandleId,
            Image = _layers.ResolveLayerImage(request.AssetId),
            X = request.Transform.X,
            Y = request.Transform.Y,
            RotationDegrees = request.Transform.RotationDegrees,
            ScaleX = request.Transform.ScaleX,
            ScaleY = request.Transform.ScaleY,
            Z = request.Z,
            DisplayMode = request.DisplayMode
        }));

    public void ReplaceLayer(string handleId, string assetId) => OnUi(() => _state.ReplaceLayer(handleId, _layers.ResolveLayerImage(assetId)));
    public void HideLayer(string handleId) => OnUi(() => _state.HideLayer(handleId));
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) => OnUi(() => _state.MoveLayer(handleId, transform, z));
    public void ShowDialogue() => OnUi(() => _state.IsDialogueVisible = true);
    public void HideDialogue() => OnUi(() => _state.IsDialogueVisible = false);

    public async Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        await OnUiAsync(async () =>
        {
            if (_state.IsNvlMode)
            {
                _state.PresentNvlLine(speaker, text);
                return;
            }

            _state.IsDialogueVisible = true;
            _page.Dialogue.Speaker = speaker;
            _page.Dialogue.Text = text;
            _page.Dialogue.CharactersPerSecond = _state.TextSpeed;
            _isTyping = true;
            try { await _page.Dialogue.StartAsync(ct); }
            finally { _isTyping = false; }
        });
    }

    public void SkipTypewriter(string widgetInstanceId) => OnUi(() => _page.Dialogue.Skip());
    public void SetVoice(string assetId) => OnUi(() => _state.StatusMessage = $"Voice requested: {assetId}");
    public Task WaitForClickAsync(CancellationToken ct) => OnUiAsync(() => _state.WaitForAdvanceAsync(ct));
    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct) => OnUiAsync(() => _state.WaitForChoiceAsync(options, ct));

    private void Advance()
    {
        if (_isTyping)
        {
            _page.Dialogue.Skip();
            return;
        }

        _state.CompleteAdvance();
    }

    public void Dispose() => _state.AdvanceRequested -= Advance;

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private static Task OnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { await action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }

    private static Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try { completion.TrySetResult(await action()); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }
}
