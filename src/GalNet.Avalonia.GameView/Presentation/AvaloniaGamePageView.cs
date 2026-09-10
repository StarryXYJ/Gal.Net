using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
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
    private readonly object _animationGate = new();
    private readonly Dictionary<string, ActiveAnimation> _activeAnimations = new(StringComparer.Ordinal);

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
            DisplayMode = request.DisplayMode,
            Opacity = request.Opacity
        }));

    public void ReplaceLayer(string handleId, string assetId) => OnUi(() => _state.ReplaceLayer(handleId, _layers.ResolveLayerImage(assetId)));
    public void HideLayer(string handleId) => OnUi(() => _state.HideLayer(handleId));
    public void MoveLayer(string handleId, LayerTransform transform, float z, float durationSec) => OnUi(() => _state.MoveLayer(handleId, transform, z));
    public async Task<AnimationOutcome> AnimateLayerAsync(LayerAnimationRequest request, CancellationToken ct)
    {
        var key = $"{request.HandleId}:{request.Property}";
        var active = new ActiveAnimation(request);
        lock (_animationGate)
        {
            if (_activeAnimations.Remove(key, out var replaced)) replaced.Complete(AnimationOutcome.Replaced);
            _activeAnimations.Add(key, active);
        }

        try
        {
            var from = request.From ?? (float)await OnUiAsync(() =>
                Task.FromResult(_state.TryGetLayerAnimationValue(request.HandleId, request.Property, out var value) ? value : double.NaN));
            if (double.IsNaN(from)) return AnimationOutcome.Replaced;
            if (request.From.HasValue) await SetAnimationValueAsync(request, from);

            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                if (active.Outcome.Task.IsCompleted) return await active.Outcome.Task;
                var progress = request.DurationSeconds <= 0 ? 1 : Math.Min(1, stopwatch.Elapsed.TotalSeconds / request.DurationSeconds);
                await SetAnimationValueAsync(request, Lerp(from, request.To, ApplyEasing(progress, request.Easing)));
                if (progress >= 1) return AnimationOutcome.Completed;
                await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(16), ct), active.Outcome.Task);
            }
        }
        finally
        {
            lock (_animationGate)
                if (_activeAnimations.TryGetValue(key, out var current) && ReferenceEquals(current, active)) _activeAnimations.Remove(key);
        }
    }

    public bool SkipLayerAnimationBatch(string? batchId)
    {
        ActiveAnimation[] matches;
        lock (_animationGate)
            matches = _activeAnimations.Values.Where(active => active.Request.Skippable &&
                (batchId is null || string.Equals(active.Request.BatchId, batchId, StringComparison.Ordinal))).ToArray();
        foreach (var animation in matches)
        {
            OnUi(() => _state.SetLayerAnimationValue(animation.Request.HandleId, animation.Request.Property, animation.Request.To));
            animation.Complete(AnimationOutcome.Skipped);
        }
        return matches.Length > 0;
    }
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

        if (SkipLayerAnimationBatch(null)) return;

        _state.CompleteAdvance();
    }

    public void Dispose() => _state.AdvanceRequested -= Advance;

    private Task SetAnimationValueAsync(LayerAnimationRequest request, double value) =>
        OnUiAsync(() =>
        {
            _state.SetLayerAnimationValue(request.HandleId, request.Property, value);
            return Task.CompletedTask;
        });

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);
    private static double ApplyEasing(double value, AnimationEasing easing) => easing switch
    {
        AnimationEasing.Step => value >= 1 ? 1 : 0,
        AnimationEasing.EaseIn => value * value,
        AnimationEasing.EaseOut => 1 - ((1 - value) * (1 - value)),
        AnimationEasing.EaseInOut => value < .5 ? 2 * value * value : 1 - (Math.Pow(-2 * value + 2, 2) / 2),
        _ => value
    };

    private sealed class ActiveAnimation(LayerAnimationRequest request)
    {
        public LayerAnimationRequest Request { get; } = request;
        public TaskCompletionSource<AnimationOutcome> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(AnimationOutcome outcome) => Outcome.TrySetResult(outcome);
    }

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
