using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GalNet.Control.Screen.Game;
using GalNet.Core.Assets;
using GalNet.Core.Settings;
using GalNet.Runtime.Logging;

namespace GalNet.Control.Runtime.Presentation;

internal sealed class DefaultTypewriterPresenter
{
    private readonly GameSettings _settings;
    private readonly GameScreenView _gameScreen;
    private readonly GameScreenViewModel _screen;
    private readonly IAssetManager? _assets;
    private CancellationTokenSource? _cts;
    private Task? _currentTask;
    private int _skipRequested;
    public Task? CurrentTask => _currentTask;
    public DefaultTypewriterPresenter(GameSettings settings, GameScreenView gameScreen, GameScreenViewModel screen, IAssetManager? assets) => (_settings, _gameScreen, _screen, _assets) = (settings, gameScreen, screen, assets);

    public async Task StartAsync(string id, string speaker, string text, CancellationToken ct)
    {
        GameLog.Logger.Information("Typewriter start: widget={WidgetId}, speaker={Speaker}, rawLength={RawLength}", id, speaker, text?.Length ?? 0);
        _cts?.Cancel(); Interlocked.Exchange(ref _skipRequested, 0); _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentTask = RunAsync(speaker, text ?? string.Empty, _cts.Token);
        try { await _currentTask; }
        catch (OperationCanceledException) { GameLog.Logger.Debug("Typewriter cancelled: widget={WidgetId}", id); }
        catch (Exception ex) { GameLog.Logger.Error(ex, "Typewriter failed: widget={WidgetId}", id); }
        finally { _currentTask = null; }
    }

    private async Task RunAsync(string speaker, string text, CancellationToken ct)
    {
        var backgroundImage = await LoadBackgroundImageAsync(_screen.Configuration.DialogueBackgroundImage);
        TextBlock content = null!;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var c = _screen.Configuration;
            var speakerBlock = new TextBlock { Text = speaker, FontSize = c.DialogueFontSize + 4, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(c.SpeakerTextColor) };
            content = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = c.DialogueFontSize, Foreground = new SolidColorBrush(c.DialogueTextColor) };
            var contentPanel = new StackPanel { Spacing = 6, Children = { speakerBlock, content } };
            var background = new Grid { ClipToBounds = true };
            if (backgroundImage is not null) background.Children.Add(new Image { Source = backgroundImage, Stretch = Stretch.UniformToFill, Opacity = Math.Clamp(c.DialogueBackgroundImageOpacity, 0, 1) });
            background.Children.Add(contentPanel);
            var box = new Border { Background = new SolidColorBrush(c.DialogueBackgroundColor), BorderBrush = Brush.Parse("#665F6075"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(c.DialogueCornerRadius), MinHeight = c.DialogueHeight, Margin = new Thickness(c.DialogueMargin), Padding = new Thickness(20, 16), BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 8, Color = Color.Parse("#66000000") }), Child = background };
            _screen.DialogueView = box; _screen.IsDialogueVisible = true; _gameScreen.ScreenOverlay.IsVisible = false;
            GameLog.Logger.Information("Typewriter dialogue created: visible={Visible}, contentParent={ContentParent}, hostParent={HostParent}", _screen.IsDialogueVisible, content.Parent is not null, _gameScreen.DialogueHost.Parent is not null);
        });
        var segments = DialogueRichTextParser.Parse(text);
        GameLog.Logger.Information("Typewriter parsed: segments={Segments}, textSegments={TextSegments}, characters={Characters}", segments.Count, segments.Count(segment => segment.Kind == DialogueSegmentKind.Text), segments.Where(segment => segment.Kind == DialogueSegmentKind.Text).Sum(segment => segment.Text.Length));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            InitializeRuns(content, segments);
            GameLog.Logger.Information("Typewriter inlines initialized: count={InlineCount}, contentParent={ContentParent}", content.Inlines?.Count ?? 0, content.Parent is not null);
        });
        var delay = _settings.TextSpeed > 0 ? (int)(1000.0 / _settings.TextSpeed) : 30;
        try
        {
            foreach (var segment in segments)
            {
                ct.ThrowIfCancellationRequested();
                if (segment.Kind == DialogueSegmentKind.Delay)
                {
                    if (segment.DelayMilliseconds > 0) await Task.Delay(segment.DelayMilliseconds, ct);
                    continue;
                }
                if (segment.Kind == DialogueSegmentKind.Instant)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => RevealAll(segments));
                    return;
                }
                if (segment.Kind != DialogueSegmentKind.Text)
                    continue;
                for (var index = 1; index <= segment.Text.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var count = index;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        segment.Run!.Text = segment.Text[..count];
                        if (count == 1)
                            GameLog.Logger.Debug("Typewriter segment rendering: length={Length}, contentParent={ContentParent}, runText={RunText}", segment.Text.Length, content.Parent is not null, segment.Run.Text);
                    });
                    if (delay > 0) await Task.Delay(delay, ct);
                }
            }
        }
        catch (OperationCanceledException) when (Volatile.Read(ref _skipRequested) != 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() => RevealAll(segments));
            GameLog.Logger.Debug("Typewriter skipped: characters={Characters}", segments.Where(segment => segment.Kind == DialogueSegmentKind.Text).Sum(segment => segment.Text.Length));
        }
        GameLog.Logger.Information("Typewriter completed: characters={Characters}", segments.Where(segment => segment.Kind == DialogueSegmentKind.Text).Sum(segment => segment.Text.Length));
    }

    private static void InitializeRuns(TextBlock target, IReadOnlyList<DialogueSegment> segments)
    {
        var inlines = target.Inlines ?? throw new InvalidOperationException("TextBlock did not provide an inline collection.");
        inlines.Clear();
        foreach (var segment in segments)
        {
            if (segment.Kind == DialogueSegmentKind.LineBreak)
            {
                inlines.Add(new LineBreak());
                continue;
            }
            if (segment.Kind != DialogueSegmentKind.Text) continue;
            var run = new Run
            {
                Text = string.Empty,
                FontWeight = segment.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = segment.Italic ? FontStyle.Italic : FontStyle.Normal
            };
            if (segment.Color is { } color && Color.TryParse(color, out var parsed))
                run.Foreground = new SolidColorBrush(parsed);
            segment.Run = run;
            inlines.Add(run);
        }
    }

    private static void RevealAll(IEnumerable<DialogueSegment> segments)
    {
        foreach (var segment in segments)
            if (segment.Kind == DialogueSegmentKind.Text) segment.Run!.Text = segment.Text;
    }

    private async Task<Bitmap?> LoadBackgroundImageAsync(string? assetId)
    {
        if (_assets is null || string.IsNullOrWhiteSpace(assetId)) return null;
        try
        {
            var file = await _assets.GetFileAsync(assetId);
            if (file?.Type != ResourceType.Sprite) return null;
            using var stream = new MemoryStream(await file.ReadAllBytesAsync());
            return new Bitmap(stream);
        }
        catch { return null; }
    }
    public void Skip(string id) { Interlocked.Exchange(ref _skipRequested, 1); _cts?.Cancel(); }
    public void Cancel() => _cts?.Cancel();
    public void SetVoice(string id) { }
}
