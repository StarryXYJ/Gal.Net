using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;

namespace GalNet.Avalonia.GameView.Controls;

/// <summary>One active card per anchor. App resources named TextCardTheme.{styleName} customize each card.</summary>
public sealed class TextCardHost : Grid, ITextCardPresenter
{
    private readonly Dictionary<TextCardPosition, Slot> _active = [];
    private readonly HashSet<Slot> _all = [];
    public TimeSpan EnterDuration { get; set; } = TimeSpan.FromSeconds(.8);
    public TimeSpan ExitDuration { get; set; } = TimeSpan.FromSeconds(.8);
    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromSeconds(2);
    public Func<string, TextCardStyle?>? ResolveStyle { get; set; }

    public TextCardHost() { IsHitTestVisible = false; ClipToBounds = true; Margin = new Thickness(24, 24, 24, 88); }

    public async Task ShowTextCardAsync(TextCardRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Position) || request.HoldSeconds is { } seconds && (!float.IsFinite(seconds) || seconds < 0))
            throw new ArgumentException("Invalid card position or hold duration.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Text)) return;
        Slot? slot = null;
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var style = ResolveStyle?.Invoke(request.StyleName) ?? new(EnterDuration, HoldDuration, ExitDuration);
            var card = new ContentControl { Content = request.Text, RenderTransform = new TranslateTransform(), Opacity = 0 };
            if (this.TryFindResource("TextCardTheme." + request.StyleName, out var theme) && theme is ControlTheme selected) card.Theme = selected;
            else if (this.TryFindResource("TextCardTheme.default", out var fallback) && fallback is ControlTheme defaultTheme) card.Theme = defaultTheme;
            else card.Content = new Border { Background = new SolidColorBrush(Colors.Black, .84), CornerRadius = new CornerRadius(6), Padding = new Thickness(20, 12),
                Child = new TextBlock { Text = request.Text, Foreground = Brushes.White, FontSize = 20, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 } };
            var column = request.Position is TextCardPosition.TopLeft or TextCardPosition.Left or TextCardPosition.BottomLeft ? -1 :
                request.Position is TextCardPosition.TopRight or TextCardPosition.Right or TextCardPosition.BottomRight ? 1 : 0;
            card.HorizontalAlignment = column < 0 ? HorizontalAlignment.Left : column > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            card.VerticalAlignment = request.Position is TextCardPosition.TopLeft or TextCardPosition.TopCenter or TextCardPosition.TopRight ? VerticalAlignment.Top :
                request.Position is TextCardPosition.BottomLeft or TextCardPosition.BottomCenter or TextCardPosition.BottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            if (_active.TryGetValue(request.Position, out var previous)) previous.Replaced.Cancel();
            slot = new(card, column * style.Distance, style);
            _active[request.Position] = slot; _all.Add(slot); Children.Add(card);
        });
        var current = slot!;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, current.Cleared.Token);
        using var hold = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, current.Replaced.Token);
        try
        {
            try
            {
                await Animate(current, entering: true, hold.Token);
                await Task.Delay(request.HoldSeconds is { } duration ? TimeSpan.FromSeconds(duration) : current.Style.Hold, hold.Token);
            }
            catch (OperationCanceledException) when (hold.IsCancellationRequested) { }
            if (!lifetime.IsCancellationRequested) await Animate(current, entering: false, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Children.Remove(current.Card); _all.Remove(current);
                if (_active.GetValueOrDefault(request.Position) == current) _active.Remove(request.Position);
            });
            current.Replaced.Dispose(); current.Cleared.Dispose();
        }
    }

    private static async Task Animate(Slot slot, bool entering, CancellationToken token)
    {
        var duration = entering ? slot.Style.Enter : slot.Style.Exit;
        await GalNet.Avalonia.GameView.Presentation.AvaloniaUiDispatcher.Instance.InvokeAsync(async () =>
        {
            if (duration <= TimeSpan.Zero) { slot.Card.Opacity = entering ? 1 : 0; return; }
            var animation = new Animation { Duration = duration, FillMode = FillMode.Forward, Easing = new CubicEaseOut(),
                Children = {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, entering ? 0d : slot.Card.Opacity), new Setter(TranslateTransform.XProperty, entering ? slot.Offset : 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, entering ? 1d : 0d), new Setter(TranslateTransform.XProperty, entering ? 0d : slot.Offset) } }
                } };
            await animation.RunAsync(slot.Card, token);
        });
    }

    public void ClearTextCards()
    {
        global::Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
        foreach (var slot in _all.ToArray()) slot.Cleared.Cancel();
        _active.Clear(); Children.Clear();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { ClearTextCards(); base.OnDetachedFromVisualTree(e); }
    private sealed record Slot(ContentControl Card, double Offset, TextCardStyle Style)
    {
        public CancellationTokenSource Replaced { get; } = new();
        public CancellationTokenSource Cleared { get; } = new();
    }
}

public sealed record TextCardStyle(TimeSpan Enter, TimeSpan Hold, TimeSpan Exit, double Distance = 320);
